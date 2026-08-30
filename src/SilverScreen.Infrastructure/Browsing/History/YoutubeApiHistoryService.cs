using Serilog;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Infrastructure.YouTube;
using YoutubeAPI.Exceptions;
using YoutubeAPI.Models.Continuations;
using YoutubeAPI.Models.Feeds;

namespace SilverScreen.Infrastructure.Browsing.History;

/// <summary>Keeps the current YoutubeAPI watch-history page sequence for the active session.</summary>
public sealed class YoutubeApiHistoryService : IAuthenticatedHistoryService, IDisposable
{
    private const string AuthenticationRequiredMessage = "Sign in to YouTube to load your watch history.";
    private const string AuthenticationRejectedMessage = "The YouTube session was rejected or has expired.";
    private const string EmptyHistoryMessage = "No watch history was returned.";
    private const string NoContinuationMessage = "No additional watch history is available.";
    private const string InvalidContinuationMessage = "Invalid history continuation.";
    private const string SuccessMessage = "Watch history loaded.";

    private static readonly ILogger Logger = Log.ForContext<YoutubeApiHistoryService>();
    private readonly IYouTubeClientProvider _clientProvider;
    private readonly Lock _lock = new();
    private readonly List<SilverScreen.Core.Browsing.Common.VideoSummary> _loadedVideos = [];
    private readonly ISessionService _sessionService;
    private string? _continuationToken;

    public YoutubeApiHistoryService(ISessionService sessionService, IYouTubeClientProvider clientProvider)
    {
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _clientProvider = clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));
        _sessionService.SessionChanged += OnSessionChanged;
    }

    public async Task<AuthenticatedHistoryResult> LoadFirstPageAsync(
        int count = VideoFeedConstants.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Max(count, 1);
        if (!IsSessionActive())
        {
            ClearCachedResults();
            return new AuthenticatedHistoryResult(
                AuthenticatedHistoryStatus.AuthenticationRequired,
                FeedPage.Empty,
                AuthenticationRequiredMessage);
        }

        return await FetchPageAsync(null, pageSize, true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AuthenticatedHistoryResult> LoadNextPageAsync(
        int count = VideoFeedConstants.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Max(count, 1);
        if (!IsSessionActive())
        {
            ClearCachedResults();
            return new AuthenticatedHistoryResult(
                AuthenticatedHistoryStatus.AuthenticationRequired,
                FeedPage.Empty,
                AuthenticationRequiredMessage);
        }

        string? token;
        lock (_lock)
            token = _continuationToken;

        if (string.IsNullOrWhiteSpace(token))
            return new AuthenticatedHistoryResult(
                AuthenticatedHistoryStatus.Empty,
                GetHistory(),
                NoContinuationMessage);

        HistoryContinuation continuation;
        try
        {
            continuation = HistoryContinuation.Import(token);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            Logger.Warning(exception, "Could not import history continuation");
            return new AuthenticatedHistoryResult(
                AuthenticatedHistoryStatus.Empty,
                GetHistory(),
                InvalidContinuationMessage);
        }

        return await FetchPageAsync(continuation, pageSize, false, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _sessionService.SessionChanged -= OnSessionChanged;

    private async Task<AuthenticatedHistoryResult> FetchPageAsync(
        HistoryContinuation? continuation,
        int pageSize,
        bool isFirstPage,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = continuation is null
                ? await _clientProvider.GetClient().Feeds.GetHistoryPageAsync(cancellationToken).ConfigureAwait(false)
                : await _clientProvider.GetClient().Feeds.GetHistoryPageAsync(continuation, cancellationToken)
                    .ConfigureAwait(false);
            var videos = page.Items
                .Select(entry => entry.Item)
                .OfType<VideoFeedItem>()
                .Where(item => !item.Video.IsShort)
                .Take(pageSize)
                .Select(item => MapVideo(item.Video, item.PlaybackProgress))
                .ToArray();
            var nextToken = page.Next?.Export();

            if (videos.Length == 0 && isFirstPage)
            {
                ClearCachedResults();
                return new AuthenticatedHistoryResult(
                    AuthenticatedHistoryStatus.Empty,
                    FeedPage.Empty,
                    EmptyHistoryMessage);
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

            return new AuthenticatedHistoryResult(
                AuthenticatedHistoryStatus.Success,
                new FeedPage(videos, nextToken),
                SuccessMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (YouTubeException exception) when (IsAuthenticationFailure(exception))
        {
            Logger.Warning(exception, "YoutubeAPI rejected authentication while loading watch history");
            ClearCachedResults();
            return new AuthenticatedHistoryResult(
                AuthenticatedHistoryStatus.AuthenticationRejected,
                FeedPage.Empty,
                AuthenticationRejectedMessage);
        }
        catch (YouTubeException exception)
        {
            Logger.Warning(exception, "YoutubeAPI failed while loading watch history");
            return new AuthenticatedHistoryResult(
                AuthenticatedHistoryStatus.TemporaryBackendFailure,
                FeedPage.Empty,
                exception.Message);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Unexpected failure while loading watch history");
            return new AuthenticatedHistoryResult(
                AuthenticatedHistoryStatus.TemporaryBackendFailure,
                FeedPage.Empty,
                exception.Message);
        }
    }

    private FeedPage GetHistory()
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

    private void ClearCachedResults()
    {
        lock (_lock)
        {
            _loadedVideos.Clear();
            _continuationToken = null;
        }
    }

    private void OnSessionChanged(object? sender, EventArgs e) => ClearCachedResults();

    private static bool IsAuthenticationFailure(YouTubeException exception) =>
        exception is AuthenticationRequiredException or AuthenticationExpiredException or PermissionDeniedException;

    private static SilverScreen.Core.Browsing.Common.VideoSummary MapVideo(
        YoutubeAPI.Models.Videos.VideoSummary video,
        YoutubeAPI.Models.Videos.VideoPlaybackProgress? playbackProgress)
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
            channel.Url.ToString(),
            YouTubePlaybackProgressMapper.Map(playbackProgress));
    }
}
