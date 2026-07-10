using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Features.Feed;

public sealed class AuthenticatedHomeFeedService : IAuthenticatedHomeFeedService, IDisposable
{
    private readonly IYouTubeHomeClient _homeClient;
    private readonly ISessionService _sessionService;
    private readonly object _lock = new();

    // Cumulative cache of loaded videos in the current manual session
    private readonly List<VideoSummary> _loadedVideos = new();
    private string? _continuationToken;
    private FeedPage _cachedFeedPage = FeedPage.Empty;
    private const string AuthenticationRequiredMessage = "Sign in with a manual YouTube session to load recommendations.";
    private const string AuthenticationRejectedMessage = "The YouTube session was rejected or has expired.";
    private const string BackendFailureMessage = "Recommendations are temporarily unavailable.";
    private const string EmptyFeedMessage = "No usable recommendations were returned.";
    private const string NoContinuationMessage = "No additional recommendations are available.";
    private const string SuccessMessage = "Recommendations loaded.";

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
            return new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.AuthenticationRequired, FeedPage.Empty, AuthenticationRequiredMessage);
        }

        try
        {
            var clientResult = await _homeClient.GetHomeFeedAsync(null, cancellationToken);
            return ProcessClientResult(clientResult, isFirstPage: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            ClearCachedResults();
            return new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.TemporaryBackendFailure, FeedPage.Empty, BackendFailureMessage);
        }
    }

    public async Task<AuthenticatedHomeFeedResult> LoadNextPageAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSessionActive())
        {
            ClearCachedResults();
            return new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.AuthenticationRequired, FeedPage.Empty, AuthenticationRequiredMessage);
        }

        string? currentToken;
        lock (_lock)
        {
            currentToken = _continuationToken;
        }

        if (string.IsNullOrEmpty(currentToken))
        {
            return new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.Empty, GetHomeFeed(), NoContinuationMessage);
        }

        try
        {
            var clientResult = await _homeClient.GetHomeFeedAsync(currentToken, cancellationToken);
            return ProcessClientResult(clientResult, isFirstPage: false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            ClearCachedResults();
            return new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.TemporaryBackendFailure, FeedPage.Empty, BackendFailureMessage);
        }
    }

    private bool IsSessionActive()
    {
        var session = _sessionService.GetCurrentSession();
        var cookies = _sessionService.GetManualSessionCookies();
        return session.IsSignedIn && session.HasManualSession && cookies != null && !string.IsNullOrWhiteSpace(cookies.Content);
    }

    private AuthenticatedHomeFeedResult ProcessClientResult(HomeFeedResult clientResult, bool isFirstPage)
    {
        if (!clientResult.IsSuccess)
        {
            ClearCachedResults();
            return clientResult.RequiresAuthentication
                ? new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.AuthenticationRejected, FeedPage.Empty, AuthenticationRejectedMessage)
                : new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.TemporaryBackendFailure, FeedPage.Empty, BackendFailureMessage);
        }

        var usableVideos = clientResult.Videos.Where(video => !video.IsShort).ToArray();
        if (usableVideos.Length == 0)
        {
            ClearCachedResults();
            return new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.Empty, FeedPage.Empty, EmptyFeedMessage);
        }

        lock (_lock)
        {
            if (isFirstPage)
            {
                _loadedVideos.Clear();
            }

            foreach (var video in usableVideos)
            {
                if (_loadedVideos.All(existingVideo => existingVideo.Id != video.Id))
                {
                    _loadedVideos.Add(video);
                }
            }

            _continuationToken = clientResult.ContinuationToken;
            _cachedFeedPage = new FeedPage(_loadedVideos.ToArray(), _continuationToken);
        }

        return new AuthenticatedHomeFeedResult(
            AuthenticatedHomeFeedStatus.Success,
            new FeedPage(usableVideos, clientResult.ContinuationToken),
            SuccessMessage);
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

    public void Dispose()
    {
        _sessionService.SessionChanged -= OnSessionChanged;
    }
}
