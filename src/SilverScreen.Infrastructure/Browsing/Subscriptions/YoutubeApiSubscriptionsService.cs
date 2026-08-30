using Serilog;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Subscriptions;
using YoutubeAPI.Models.Channels;
using SilverScreen.Infrastructure.YouTube;
using YoutubeAPI.Exceptions;
using YoutubeAPI.Models.Continuations;
using YoutubeAPI.Models.Feeds;

namespace SilverScreen.Infrastructure.Browsing.Subscriptions;

/// <summary>Keeps the current YoutubeAPI subscriptions and subscribed-channel pages for the active session.</summary>
public sealed class YoutubeApiSubscriptionsService : IAuthenticatedSubscriptionsService, IDisposable
{
    private const string AuthenticationRequiredMessage = "Sign in to YouTube to load your subscriptions.";
    private const string AuthenticationRejectedMessage = "The YouTube session was rejected or has expired.";
    private const string EmptySubscriptionsMessage = "No subscription videos were returned.";
    private const string NoContinuationMessage = "No additional subscription videos are available.";
    private const string InvalidContinuationMessage = "Invalid subscription continuation.";
    private const string SuccessMessage = "Subscriptions loaded.";
    private const string ChannelsSuccessMessage = "Subscribed channels loaded.";
    private const string ChannelsEmptyMessage = "No subscribed channels were returned.";

    private static readonly ILogger Logger = Log.ForContext<YoutubeApiSubscriptionsService>();
    private readonly IYouTubeClientProvider _clientProvider;
    private readonly Lock _lock = new();
    private readonly List<SilverScreen.Core.Browsing.Common.VideoSummary> _loadedVideos = [];
    private readonly ISessionService _sessionService;
    private string? _continuationToken;

    public YoutubeApiSubscriptionsService(ISessionService sessionService, IYouTubeClientProvider clientProvider)
    {
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _clientProvider = clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));
        _sessionService.SessionChanged += OnSessionChanged;
    }

    public async Task<AuthenticatedSubscriptionsFeedResult> LoadFirstFeedPageAsync(
        int count = VideoFeedConstants.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Max(count, 1);
        if (!IsSessionActive())
        {
            ClearCachedResults();
            return new AuthenticatedSubscriptionsFeedResult(
                AuthenticatedSubscriptionsStatus.AuthenticationRequired,
                FeedPage.Empty,
                AuthenticationRequiredMessage);
        }

        return await FetchFeedPageAsync(null, pageSize, true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AuthenticatedSubscriptionsFeedResult> LoadNextFeedPageAsync(
        int count = VideoFeedConstants.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Max(count, 1);
        if (!IsSessionActive())
        {
            ClearCachedResults();
            return new AuthenticatedSubscriptionsFeedResult(
                AuthenticatedSubscriptionsStatus.AuthenticationRequired,
                FeedPage.Empty,
                AuthenticationRequiredMessage);
        }

        string? token;
        lock (_lock)
            token = _continuationToken;

        if (string.IsNullOrWhiteSpace(token))
            return new AuthenticatedSubscriptionsFeedResult(
                AuthenticatedSubscriptionsStatus.Empty,
                GetFeed(),
                NoContinuationMessage);

        SubscriptionsContinuation continuation;
        try
        {
            continuation = SubscriptionsContinuation.Import(token);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            Logger.Warning(exception, "Could not import subscriptions continuation");
            return new AuthenticatedSubscriptionsFeedResult(
                AuthenticatedSubscriptionsStatus.Empty,
                GetFeed(),
                InvalidContinuationMessage);
        }

        return await FetchFeedPageAsync(continuation, pageSize, false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SubscribedChannelsResult> LoadSubscribedChannelsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsSessionActive())
        {
            ClearCachedResults();
            return new SubscribedChannelsResult(
                AuthenticatedSubscriptionsStatus.AuthenticationRequired,
                [],
                AuthenticationRequiredMessage);
        }

        try
        {
            // Channel pages have their own typed continuation protocol. The first page is the complete
            // channel-page request exposed by the Core contract; unlike the video feed it is not accumulated.
            var page = await _clientProvider.GetClient().Feeds.GetSubscribedChannelsPageAsync(cancellationToken)
                .ConfigureAwait(false);
            var channels = page.Items.Select(MapChannel).ToArray();
            return new SubscribedChannelsResult(
                channels.Length > 0 ? AuthenticatedSubscriptionsStatus.Success : AuthenticatedSubscriptionsStatus.Empty,
                channels,
                channels.Length > 0 ? ChannelsSuccessMessage : ChannelsEmptyMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (YouTubeException exception) when (IsAuthenticationFailure(exception))
        {
            Logger.Warning(exception, "YoutubeAPI rejected authentication while loading subscribed channels");
            ClearCachedResults();
            return new SubscribedChannelsResult(
                AuthenticatedSubscriptionsStatus.AuthenticationRejected,
                [],
                AuthenticationRejectedMessage);
        }
        catch (YouTubeException exception)
        {
            Logger.Warning(exception, "YoutubeAPI failed while loading subscribed channels");
            return new SubscribedChannelsResult(
                AuthenticatedSubscriptionsStatus.TemporaryBackendFailure,
                [],
                exception.Message);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Unexpected failure while loading subscribed channels");
            return new SubscribedChannelsResult(
                AuthenticatedSubscriptionsStatus.TemporaryBackendFailure,
                [],
                exception.Message);
        }
    }

    public void Dispose() => _sessionService.SessionChanged -= OnSessionChanged;

    private async Task<AuthenticatedSubscriptionsFeedResult> FetchFeedPageAsync(
        SubscriptionsContinuation? continuation,
        int pageSize,
        bool isFirstPage,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = continuation is null
                ? await _clientProvider.GetClient().Feeds.GetSubscriptionsPageAsync(cancellationToken)
                    .ConfigureAwait(false)
                : await _clientProvider.GetClient().Feeds.GetSubscriptionsPageAsync(continuation, cancellationToken)
                    .ConfigureAwait(false);
            var videos = page.Items
                .OfType<VideoFeedItem>()
                .Select(item => item.Video)
                .Where(video => !video.IsShort)
                .Take(pageSize)
                .Select(MapVideo)
                .ToArray();
            var nextToken = page.Next?.Export();

            if (videos.Length == 0 && isFirstPage)
            {
                ClearCachedFeed();
                return new AuthenticatedSubscriptionsFeedResult(
                    AuthenticatedSubscriptionsStatus.Empty,
                    FeedPage.Empty,
                    EmptySubscriptionsMessage);
            }

            lock (_lock)
            {
                if (isFirstPage)
                    _loadedVideos.Clear();

                foreach (var video in videos)
                    if (_loadedVideos.All(existing => existing.Id != video.Id))
                        _loadedVideos.Add(video);

                _continuationToken = nextToken;
            }

            return new AuthenticatedSubscriptionsFeedResult(
                AuthenticatedSubscriptionsStatus.Success,
                new FeedPage(videos, nextToken),
                SuccessMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (YouTubeException exception) when (IsAuthenticationFailure(exception))
        {
            Logger.Warning(exception, "YoutubeAPI rejected authentication while loading subscriptions");
            ClearCachedResults();
            return new AuthenticatedSubscriptionsFeedResult(
                AuthenticatedSubscriptionsStatus.AuthenticationRejected,
                FeedPage.Empty,
                AuthenticationRejectedMessage);
        }
        catch (YouTubeException exception)
        {
            Logger.Warning(exception, "YoutubeAPI failed while loading subscriptions");
            return new AuthenticatedSubscriptionsFeedResult(
                AuthenticatedSubscriptionsStatus.TemporaryBackendFailure,
                FeedPage.Empty,
                exception.Message);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Unexpected failure while loading subscriptions");
            return new AuthenticatedSubscriptionsFeedResult(
                AuthenticatedSubscriptionsStatus.TemporaryBackendFailure,
                FeedPage.Empty,
                exception.Message);
        }
    }

    private FeedPage GetFeed()
    {
        lock (_lock)
            return new FeedPage([.. _loadedVideos], _continuationToken);
    }

    private bool IsSessionActive()
    {
        var session = _sessionService.GetCurrentSession();
        var cookies = _sessionService.GetManualSessionCookies();
        return session is { IsSignedIn: true, HasManualSession: true } &&
               cookies is { Content: not null } && !string.IsNullOrWhiteSpace(cookies.Content);
    }

    private void ClearCachedFeed()
    {
        lock (_lock)
        {
            _loadedVideos.Clear();
            _continuationToken = null;
        }
    }

    private void ClearCachedResults() => ClearCachedFeed();

    private void OnSessionChanged(object? sender, EventArgs e) => ClearCachedResults();

    private static bool IsAuthenticationFailure(YouTubeException exception) =>
        exception is AuthenticationRequiredException or AuthenticationExpiredException or PermissionDeniedException;

    private static SubscribedChannel MapChannel(ChannelSummary channel)
    {
        var avatarUrl = channel.Thumbnails.FirstOrDefault()?.Url.ToString();
        return new SubscribedChannel(
            channel.Id.Value,
            channel.Title,
            channel.Url.ToString(),
            avatarUrl,
            null,
            channel.SubscriberCount);
    }

    private static SilverScreen.Core.Browsing.Common.VideoSummary MapVideo(
        YoutubeAPI.Models.Videos.VideoSummary video)
    {
        var thumbnail = video.Thumbnails
            .OrderBy(item => (long)item.Width * item.Height)
            .LastOrDefault()?.Url.ToString() ?? string.Empty;
        var channel = video.Channel;
        return new SilverScreen.Core.Browsing.Common.VideoSummary(
            video.Id.Value,
            video.Title,
            channel.Title,
            video.Duration ?? TimeSpan.Zero,
            thumbnail,
            video.IsShort,
            video.Url.ToString(),
            video.PublishedAt is { } publishedAt ? DateOnly.FromDateTime(publishedAt.UtcDateTime) : null,
            video.PublishedAt,
            channel.Url.ToString());
    }
}
