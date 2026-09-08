using Serilog;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Infrastructure.YouTube;
using YoutubeAPI.Exceptions;
using YoutubeAPI.Models.Continuations;
using YoutubeAPI.Models.Feeds;
using YoutubeAPI.Models.Videos;
using VideoSummary = SilverScreen.Core.Browsing.Common.VideoSummary;

namespace SilverScreen.Infrastructure.Browsing.Home;

/// <summary>Keeps the current YoutubeAPI home-feed page sequence for the active session.</summary>
public sealed class YoutubeApiHomeFeedService : IAuthenticatedHomeFeedService, IDisposable
{
    private const string EmptyFeedMessage = "No usable recommendations were returned.";
    private const string NoContinuationMessage = "No additional recommendations are available.";
    private const string InvalidContinuationMessage = "Invalid recommendation continuation.";
    private const string SuccessMessage = "Recommendations loaded.";

    private static readonly ILogger Logger = Log.ForContext<YoutubeApiHomeFeedService>();
    private readonly IYouTubeClientProvider _clientProvider;
    private readonly List<VideoSummary> _loadedVideos = [];
    private readonly Lock _lock = new();
    private readonly ISessionService _sessionService;
    private FeedPage _cachedFeedPage = FeedPage.Empty;
    private string? _continuationToken;

    public YoutubeApiHomeFeedService(ISessionService sessionService, IYouTubeClientProvider clientProvider)
    {
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _clientProvider = clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));
        _sessionService.SessionChanged += OnSessionChanged;
    }

    public async Task<AuthenticatedHomeFeedResult> LoadFirstPageAsync(
        int count = VideoFeedConstants.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Max(count, 1);
        if (IsSessionActive())
            return await FetchPageAsync(null, pageSize, true, cancellationToken).ConfigureAwait(false);
        ClearCachedResults();
        return new AuthenticatedHomeFeedResult(
            AuthenticatedHomeFeedStatus.AuthenticationRequired,
            FeedPage.Empty,
            SessionGate.HomeServiceAuthenticationRequiredMessage);
    }

    public async Task<AuthenticatedHomeFeedResult> LoadNextPageAsync(
        int count = VideoFeedConstants.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Max(count, 1);
        if (!IsSessionActive())
        {
            ClearCachedResults();
            return new AuthenticatedHomeFeedResult(
                AuthenticatedHomeFeedStatus.AuthenticationRequired,
                FeedPage.Empty,
                SessionGate.HomeServiceAuthenticationRequiredMessage);
        }

        string? token;
        lock (_lock)
        {
            token = _continuationToken;
        }

        if (string.IsNullOrWhiteSpace(token))
            return new AuthenticatedHomeFeedResult(
                AuthenticatedHomeFeedStatus.Empty,
                GetHomeFeed(),
                NoContinuationMessage);

        HomeContinuation continuation;
        try
        {
            continuation = HomeContinuation.Import(token);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            Logger.Warning(exception, "Could not import home feed continuation");
            return new AuthenticatedHomeFeedResult(
                AuthenticatedHomeFeedStatus.Empty,
                GetHomeFeed(),
                InvalidContinuationMessage);
        }

        return await FetchPageAsync(continuation, pageSize, false, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _sessionService.SessionChanged -= OnSessionChanged;
    }

    private async Task<AuthenticatedHomeFeedResult> FetchPageAsync(
        HomeContinuation? continuation,
        int pageSize,
        bool isFirstPage,
        CancellationToken cancellationToken)
    {
        try
        {
            var videos = new List<VideoSummary>();
            var knownVideoIds = new HashSet<string>(StringComparer.Ordinal);
            if (!isFirstPage)
                lock (_lock)
                {
                    foreach (var video in _loadedVideos)
                        knownVideoIds.Add(video.Id);
                }

            var currentContinuation = continuation;
            string? nextToken;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var currentToken = currentContinuation?.Export();
                var page = currentContinuation is null
                    ? await _clientProvider.GetClient().Feeds.GetHomePageAsync(cancellationToken)
                        .ConfigureAwait(false)
                    : await _clientProvider.GetClient().Feeds.GetHomePageAsync(currentContinuation, cancellationToken)
                        .ConfigureAwait(false);

                videos.AddRange(page.Items.OfType<VideoFeedItem>().Where(item => !item.Video.IsShort)
                    .Select(item => MapVideo(item.Video, item.PlaybackProgress))
                    .Where(video => knownVideoIds.Add(video.Id)));

                nextToken = page.Next?.Export();
                if (!isFirstPage || videos.Count >= pageSize || string.IsNullOrWhiteSpace(nextToken))
                {
                    if (string.IsNullOrWhiteSpace(nextToken))
                        nextToken = null;
                    break;
                }

                // A repeated continuation cannot produce any more progress and would otherwise
                // make a malformed response spin forever.
                if (string.Equals(nextToken, currentToken, StringComparison.Ordinal))
                {
                    nextToken = null;
                    break;
                }

                currentContinuation = page.Next;
            } while (true);

            if (videos.Count != 0 || !isFirstPage) return CommitVideos(videos, nextToken, isFirstPage);
            ClearCachedResults();
            return new AuthenticatedHomeFeedResult(
                AuthenticatedHomeFeedStatus.Empty,
                FeedPage.Empty,
                EmptyFeedMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (YouTubeException exception) when (IsAuthenticationFailure(exception))
        {
            Logger.Warning(exception, "YoutubeAPI rejected authentication while loading home recommendations");
            ClearCachedResults();
            return new AuthenticatedHomeFeedResult(
                AuthenticatedHomeFeedStatus.AuthenticationRejected,
                FeedPage.Empty,
                SessionGate.ServiceAuthenticationRejectedMessage);
        }
        catch (YouTubeException exception)
        {
            Logger.Warning(exception, "YoutubeAPI failed while loading home recommendations");
            return new AuthenticatedHomeFeedResult(
                AuthenticatedHomeFeedStatus.TemporaryBackendFailure,
                FeedPage.Empty,
                exception.Message);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Unexpected failure while loading home recommendations");
            return new AuthenticatedHomeFeedResult(
                AuthenticatedHomeFeedStatus.TemporaryBackendFailure,
                FeedPage.Empty,
                exception.Message);
        }
    }

    private AuthenticatedHomeFeedResult CommitVideos(
        IReadOnlyList<VideoSummary> videos,
        string? nextToken,
        bool isFirstPage)
    {
        lock (_lock)
        {
            if (isFirstPage)
                _loadedVideos.Clear();

            foreach (var video in videos)
                if (_loadedVideos.All(existing => existing.Id != video.Id))
                    _loadedVideos.Add(video);

            _continuationToken = nextToken;
            _cachedFeedPage = new FeedPage([.. _loadedVideos], _continuationToken);
        }

        return new AuthenticatedHomeFeedResult(
            AuthenticatedHomeFeedStatus.Success,
            new FeedPage(videos, nextToken),
            SuccessMessage);
    }

    private FeedPage GetHomeFeed()
    {
        lock (_lock)
        {
            return _cachedFeedPage;
        }
    }

    private bool IsSessionActive()
    {
        return SessionGate.RequireSignedIn(_sessionService);
    }

    private void ClearCachedResults()
    {
        lock (_lock)
        {
            _loadedVideos.Clear();
            _continuationToken = null;
            _cachedFeedPage = FeedPage.Empty;
        }
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        ClearCachedResults();
    }

    private static bool IsAuthenticationFailure(YouTubeException exception)
    {
        return exception is AuthenticationRequiredException or AuthenticationExpiredException
            or PermissionDeniedException;
    }

    private static VideoSummary MapVideo(
        YoutubeAPI.Models.Videos.VideoSummary video,
        VideoPlaybackProgress? playbackProgress)
    {
        var thumbnail = video.Thumbnails
            .OrderBy(item => (long)item.Width * item.Height)
            .LastOrDefault()?.Url.ToString() ?? string.Empty;
        var channel = video.Channel;
        return new VideoSummary(
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