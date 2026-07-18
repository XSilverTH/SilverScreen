using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Infrastructure.Features.Feed;

public sealed class AuthenticatedHomeFeedService : IAuthenticatedHomeFeedService, IDisposable
{
    private const string AuthenticationRequiredMessage =
        "Sign in to YouTube to load recommendations.";

    private const string AuthenticationRejectedMessage = "The YouTube session was rejected or has expired.";
    private const string BackendFailureMessage = "Recommendations are temporarily unavailable.";
    private const string EmptyFeedMessage = "No usable recommendations were returned.";
    private const string NoContinuationMessage = "No additional recommendations are available.";
    private const string SuccessMessage = "Recommendations loaded.";
    private readonly IYouTubeHomeClient _homeClient;

    // Cumulative cache of loaded videos in the current manual session
    private readonly List<VideoSummary> _loadedVideos = [];
    private readonly Lock _lock = new();
    private readonly ISessionService _sessionService;
    private FeedPage _cachedFeedPage = FeedPage.Empty;
    private string? _continuationToken;

    public AuthenticatedHomeFeedService(IYouTubeHomeClient homeClient, ISessionService sessionService)
    {
        _homeClient = homeClient ?? throw new ArgumentNullException(nameof(homeClient));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));

        _sessionService.SessionChanged += OnSessionChanged;
    }

    public FeedPage GetHomeFeed()
    {
        lock (_lock)
        {
            return _cachedFeedPage;
        }
    }

    public async Task<AuthenticatedHomeFeedResult> LoadFirstPageAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSessionActive())
        {
            ClearCachedResults();
            return new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.AuthenticationRequired, FeedPage.Empty,
                AuthenticationRequiredMessage);
        }

        try
        {
            var clientResult = await _homeClient.GetHomeFeedAsync(null, cancellationToken);
            return ProcessClientResult(clientResult, true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.TemporaryBackendFailure, FeedPage.Empty,
                BackendFailureMessage);
        }
    }

    public async Task<AuthenticatedHomeFeedResult> LoadNextPageAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSessionActive())
        {
            ClearCachedResults();
            return new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.AuthenticationRequired, FeedPage.Empty,
                AuthenticationRequiredMessage);
        }

        string? currentToken;
        lock (_lock)
        {
            currentToken = _continuationToken;
        }

        if (string.IsNullOrEmpty(currentToken))
            return new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.Empty, GetHomeFeed(),
                NoContinuationMessage);

        try
        {
            var clientResult = await _homeClient.GetHomeFeedAsync(currentToken, cancellationToken);
            return ProcessClientResult(clientResult, false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.TemporaryBackendFailure, FeedPage.Empty,
                BackendFailureMessage);
        }
    }

    public void Dispose()
    {
        _sessionService.SessionChanged -= OnSessionChanged;
    }

    private bool IsSessionActive()
    {
        var session = _sessionService.GetCurrentSession();
        var cookies = _sessionService.GetManualSessionCookies();
        return session is { IsSignedIn: true, HasManualSession: true } && cookies != null &&
               !string.IsNullOrWhiteSpace(cookies.Content);
    }

    private AuthenticatedHomeFeedResult ProcessClientResult(HomeFeedResult clientResult, bool isFirstPage)
    {
        if (!clientResult.IsSuccess)
        {
            if (!clientResult.RequiresAuthentication)
                return new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.TemporaryBackendFailure,
                    FeedPage.Empty,
                    BackendFailureMessage);
            ClearCachedResults();
            return new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.AuthenticationRejected,
                FeedPage.Empty, AuthenticationRejectedMessage);
        }

        var usableVideos = clientResult.Videos.Where(video => !video.IsShort).ToArray();
        if (usableVideos.Length == 0)
        {
            if (isFirstPage)
            {
                ClearCachedResults();
                return new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.Empty, FeedPage.Empty,
                    EmptyFeedMessage);
            }

            lock (_lock)
            {
                _continuationToken = clientResult.ContinuationToken;
                _cachedFeedPage = new FeedPage(_loadedVideos.ToArray(), _continuationToken);
            }

            return new AuthenticatedHomeFeedResult(
                AuthenticatedHomeFeedStatus.Success,
                new FeedPage(usableVideos, clientResult.ContinuationToken),
                SuccessMessage);
        }

        lock (_lock)
        {
            if (isFirstPage)
                _loadedVideos.Clear();

            foreach (var video in usableVideos)
                if (_loadedVideos.All(existingVideo => existingVideo.Id != video.Id))
                    _loadedVideos.Add(video);

            _continuationToken = clientResult.ContinuationToken;
            _cachedFeedPage = new FeedPage(_loadedVideos.ToArray(), _continuationToken);
        }

        var successMessage = clientResult.StatusMessage == "Public recommendations are displayed."
            ? clientResult.StatusMessage
            : SuccessMessage;
        return new AuthenticatedHomeFeedResult(
            AuthenticatedHomeFeedStatus.Success,
            new FeedPage(usableVideos, clientResult.ContinuationToken),
            successMessage);
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
}