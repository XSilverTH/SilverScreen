using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Infrastructure.Features.Feed;

/// <summary>Keeps the current server-backed watch-history page sequence for the active session.</summary>
public sealed class AuthenticatedHistoryService : IAuthenticatedHistoryService, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<AuthenticatedHistoryService>();
    private const string AuthenticationRequiredMessage = "Sign in to YouTube to load your watch history.";
    private const string AuthenticationRejectedMessage = "The YouTube session was rejected or has expired.";
    private const string BackendFailureMessage = "Watch history is temporarily unavailable.";
    private const string EmptyHistoryMessage = "No watch history was returned.";
    private const string NoContinuationMessage = "No additional watch history is available.";
    private const string SuccessMessage = "Watch history loaded.";
    private readonly IYouTubeHistoryClient _historyClient;
    private readonly List<VideoSummary> _loadedVideos = [];
    private readonly Lock _lock = new();
    private readonly ISessionService _sessionService;
    private string? _continuationToken;

    public AuthenticatedHistoryService(IYouTubeHistoryClient historyClient, ISessionService sessionService)
    {
        _historyClient = historyClient ?? throw new ArgumentNullException(nameof(historyClient));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _sessionService.SessionChanged += OnSessionChanged;
    }

    public async Task<AuthenticatedHistoryResult> LoadFirstPageAsync(CancellationToken cancellationToken = default)
    {
        Logger.Information("Loading first page of watch history");
        if (!IsSessionActive())
        {
            Logger.Information("No active YouTube session; returning authentication required for history");
            ClearCachedResults();
            return new AuthenticatedHistoryResult(AuthenticatedHistoryStatus.AuthenticationRequired, FeedPage.Empty,
                AuthenticationRequiredMessage);
        }

        try
        {
            return ProcessClientResult(await _historyClient.GetHistoryAsync(null, cancellationToken).ConfigureAwait(false), true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Exception while loading first page of watch history");
            return new AuthenticatedHistoryResult(AuthenticatedHistoryStatus.TemporaryBackendFailure, FeedPage.Empty,
                BackendFailureMessage);
        }
    }

    public async Task<AuthenticatedHistoryResult> LoadNextPageAsync(CancellationToken cancellationToken = default)
    {
        Logger.Information("Loading next page of watch history");
        if (!IsSessionActive())
        {
            Logger.Information("No active YouTube session; returning authentication required for history");
            ClearCachedResults();
            return new AuthenticatedHistoryResult(AuthenticatedHistoryStatus.AuthenticationRequired, FeedPage.Empty,
                AuthenticationRequiredMessage);
        }
        string? continuationToken;
        lock (_lock)
        {
            continuationToken = _continuationToken;
        }

        if (string.IsNullOrEmpty(continuationToken))
            return new AuthenticatedHistoryResult(AuthenticatedHistoryStatus.Empty, GetHistory(), NoContinuationMessage);

        try
        {
            return ProcessClientResult(
                await _historyClient.GetHistoryAsync(continuationToken, cancellationToken).ConfigureAwait(false), false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Exception while loading next page of watch history");
            return new AuthenticatedHistoryResult(AuthenticatedHistoryStatus.TemporaryBackendFailure, FeedPage.Empty,
                BackendFailureMessage);
        }
    }

    public void Dispose()
    {
        _sessionService.SessionChanged -= OnSessionChanged;
    }

    private FeedPage GetHistory()
    {
        lock (_lock)
        {
            return new FeedPage([.. _loadedVideos], _continuationToken);
        }
    }

    private bool IsSessionActive()
    {
        var session = _sessionService.GetCurrentSession();
        var cookies = _sessionService.GetManualSessionCookies();
        return session is { IsSignedIn: true, HasManualSession: true } && cookies is not null &&
               !string.IsNullOrWhiteSpace(cookies.Content);
    }

    private AuthenticatedHistoryResult ProcessClientResult(HistoryFeedResult clientResult, bool isFirstPage)
    {
        if (!clientResult.IsSuccess)
        {
            if (clientResult.RequiresAuthentication)
            {
                ClearCachedResults();
                return new AuthenticatedHistoryResult(AuthenticatedHistoryStatus.AuthenticationRejected, FeedPage.Empty,
                    AuthenticationRejectedMessage);
            }

            var message = clientResult.StatusMessage?.StartsWith("yt-dlp ", StringComparison.OrdinalIgnoreCase) == true
                ? clientResult.StatusMessage
                : BackendFailureMessage;
            return new AuthenticatedHistoryResult(AuthenticatedHistoryStatus.TemporaryBackendFailure, FeedPage.Empty, message);
        }

        var usableVideos = clientResult.Videos.Where(video => !video.IsShort).ToArray();
        if (usableVideos.Length == 0 && isFirstPage)
        {
            ClearCachedResults();
            return new AuthenticatedHistoryResult(AuthenticatedHistoryStatus.Empty, FeedPage.Empty, EmptyHistoryMessage);
        }

        lock (_lock)
        {
            if (isFirstPage)
                _loadedVideos.Clear();

            foreach (var video in usableVideos)
                if (_loadedVideos.All(existingVideo => existingVideo.Id != video.Id))
                    _loadedVideos.Add(video);

            _continuationToken = clientResult.ContinuationToken;
        }

        return new AuthenticatedHistoryResult(
            AuthenticatedHistoryStatus.Success,
            new FeedPage(usableVideos, clientResult.ContinuationToken),
            SuccessMessage);
    }

    private void ClearCachedResults()
    {
        lock (_lock)
        {
            _loadedVideos.Clear();
            _continuationToken = null;
        }
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        ClearCachedResults();
    }
}
