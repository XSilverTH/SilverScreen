using System.Globalization;
using Serilog;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Common;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Infrastructure.Browsing.History;

/// <summary>Keeps the current server-backed watch-history page sequence for the active session.</summary>
public sealed class AuthenticatedHistoryService : IAuthenticatedHistoryService, IDisposable
{
    private const int PageSize = 20;
    private const string AuthenticationRequiredMessage = "Sign in to YouTube to load your watch history.";
    private const string AuthenticationRejectedMessage = "The YouTube session was rejected or has expired.";
    private const string EmptyHistoryMessage = "No watch history was returned.";
    private const string NoContinuationMessage = "No additional watch history is available.";
    private const string SuccessMessage = "Watch history loaded.";

    private static readonly ILogger Logger = Log.ForContext<AuthenticatedHistoryService>();
    private readonly ICookieFileProvider _cookieFileProvider;

    private readonly List<VideoSummary> _loadedVideos = [];
    private readonly Lock _lock = new();
    private readonly IPreferencesService _preferencesService;
    private readonly IYtDlpRunner _runner;

    private readonly ISessionService _sessionService;
    private readonly TimeSpan _timeout;
    private string? _continuationToken;

    public AuthenticatedHistoryService(
        ISessionService sessionService,
        ICookieFileProvider cookieFileProvider,
        IPreferencesService preferencesService,
        IYtDlpRunner runner,
        TimeSpan? timeout = null)
    {
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _cookieFileProvider = cookieFileProvider ?? throw new ArgumentNullException(nameof(cookieFileProvider));
        _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _timeout = timeout ?? TimeSpan.FromSeconds(30);

        _sessionService.SessionChanged += OnSessionChanged;
    }

    public async Task<AuthenticatedHistoryResult> LoadFirstPageAsync(CancellationToken cancellationToken = default)
    {
        Logger.Information("Loading first page of watch history");
        if (IsSessionActive())
            return await FetchPageAsync(1, true, cancellationToken).ConfigureAwait(false);
        Logger.Information("No active YouTube session; returning authentication required for history");
        ClearCachedResults();
        return new AuthenticatedHistoryResult(AuthenticatedHistoryStatus.AuthenticationRequired, FeedPage.Empty,
            AuthenticationRequiredMessage);
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

        string? currentToken;
        lock (_lock)
        {
            currentToken = _continuationToken;
        }

        if (string.IsNullOrEmpty(currentToken))
            return new AuthenticatedHistoryResult(AuthenticatedHistoryStatus.Empty, GetHistory(),
                NoContinuationMessage);

        if (!int.TryParse(currentToken, out var startIndex) || startIndex < 1)
            return new AuthenticatedHistoryResult(AuthenticatedHistoryStatus.Empty, GetHistory(),
                "Invalid history continuation.");

        return await FetchPageAsync(startIndex, false, cancellationToken).ConfigureAwait(false);
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

    private async Task<AuthenticatedHistoryResult> FetchPageAsync(
        int startIndex,
        bool isFirstPage,
        CancellationToken cancellationToken)
    {
        var cookies = _sessionService.GetManualSessionCookies();
        if (cookies is null || string.IsNullOrWhiteSpace(cookies.Content))
        {
            ClearCachedResults();
            return new AuthenticatedHistoryResult(AuthenticatedHistoryStatus.AuthenticationRejected, FeedPage.Empty,
                AuthenticationRejectedMessage);
        }

        using var cookieFile = _cookieFileProvider.CreateCookieFile();
        if (cookieFile is null || string.IsNullOrWhiteSpace(cookieFile.Path))
        {
            ClearCachedResults();
            return new AuthenticatedHistoryResult(AuthenticatedHistoryStatus.AuthenticationRejected, FeedPage.Empty,
                AuthenticationRejectedMessage);
        }

        var executablePath = _preferencesService.GetPreferences().YtDlpExecutablePath;
        ProcessResult processResult;
        try
        {
            processResult = await _runner.RunAsync(
                    YtDlpCommandBuilder.BuildHistory(executablePath, startIndex, cookieFile.Path),
                    _timeout,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException exception)
        {
            Logger.Warning(exception, "yt-dlp timed out while loading watch history");
            return new AuthenticatedHistoryResult(AuthenticatedHistoryStatus.TemporaryBackendFailure, FeedPage.Empty,
                RuntimeDependencyGuidance.YtDlpTimedOut);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Could not execute yt-dlp for watch history");
            return new AuthenticatedHistoryResult(AuthenticatedHistoryStatus.TemporaryBackendFailure, FeedPage.Empty,
                RuntimeDependencyGuidance.YtDlpUnavailable(executablePath));
        }

        if (processResult.ExitCode != 0)
        {
            Logger.Warning("yt-dlp exited with code {ExitCode} while loading watch history", processResult.ExitCode);
            return new AuthenticatedHistoryResult(AuthenticatedHistoryStatus.TemporaryBackendFailure, FeedPage.Empty,
                RuntimeDependencyGuidance.YtDlpFailed($"the process exited with error code {processResult.ExitCode}."));
        }

        if (string.IsNullOrWhiteSpace(processResult.StandardOutput))
        {
            if (!isFirstPage)
                return new AuthenticatedHistoryResult(AuthenticatedHistoryStatus.Empty, GetHistory(),
                    NoContinuationMessage);
            ClearCachedResults();
            return new AuthenticatedHistoryResult(AuthenticatedHistoryStatus.Empty, FeedPage.Empty,
                EmptyHistoryMessage);
        }

        try
        {
            var pageEntries = YtDlpVideoParser.Parse(processResult.StandardOutput).ToArray();
            var usableVideos = pageEntries.Where(video => !video.IsShort).ToArray();

            if (usableVideos.Length == 0 && isFirstPage)
            {
                ClearCachedResults();
                return new AuthenticatedHistoryResult(AuthenticatedHistoryStatus.Empty, FeedPage.Empty,
                    EmptyHistoryMessage);
            }

            var nextToken = pageEntries.Length == PageSize
                ? (startIndex + PageSize).ToString(CultureInfo.InvariantCulture)
                : null;

            lock (_lock)
            {
                if (isFirstPage)
                    _loadedVideos.Clear();

                foreach (var video in usableVideos)
                    if (_loadedVideos.All(existing => existing.Id != video.Id))
                        _loadedVideos.Add(video);

                _continuationToken = nextToken;
            }

            return new AuthenticatedHistoryResult(
                AuthenticatedHistoryStatus.Success,
                new FeedPage(usableVideos, nextToken),
                SuccessMessage);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Could not parse yt-dlp output for watch history");
            return new AuthenticatedHistoryResult(AuthenticatedHistoryStatus.TemporaryBackendFailure, FeedPage.Empty,
                RuntimeDependencyGuidance.YtDlpFailed("the watch history output could not be read."));
        }
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