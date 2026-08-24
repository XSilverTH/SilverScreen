using System.Globalization;
using Serilog;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Core.Common;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Infrastructure.Browsing.Home;

public sealed class AuthenticatedHomeFeedService : IAuthenticatedHomeFeedService, IDisposable
{
    private const int PageSize = 20;
    private const string AuthenticationRequiredMessage = "Sign in to YouTube to load recommendations.";
    private const string AuthenticationRejectedMessage = "The YouTube session was rejected or has expired.";
    private const string EmptyFeedMessage = "No usable recommendations were returned.";
    private const string NoContinuationMessage = "No additional recommendations are available.";
    private const string SuccessMessage = "Recommendations loaded.";
    private const string PublicSuccessMessage = "Public recommendations are displayed.";

    private static readonly ILogger Logger = Log.ForContext<AuthenticatedHomeFeedService>();
    private readonly ICookieFileProvider _cookieFileProvider;

    private readonly List<VideoSummary> _loadedVideos = [];
    private readonly Lock _lock = new();
    private readonly IPreferencesService _preferencesService;
    private readonly IYtDlpRunner _runner;

    private readonly ISessionService _sessionService;
    private readonly TimeSpan _timeout;
    private FeedPage _cachedFeedPage = FeedPage.Empty;
    private string? _continuationToken;

    public AuthenticatedHomeFeedService(
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

    public async Task<AuthenticatedHomeFeedResult> LoadFirstPageAsync(CancellationToken cancellationToken = default)
    {
        Logger.Information("Loading first page of authenticated home feed");
        if (IsSessionActive())
            return await FetchPageAsync(1, true, cancellationToken).ConfigureAwait(false);
        Logger.Information("No active YouTube session; returning authentication required status");
        ClearCachedResults();
        return new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.AuthenticationRequired, FeedPage.Empty,
            AuthenticationRequiredMessage);
    }

    public async Task<AuthenticatedHomeFeedResult> LoadNextPageAsync(CancellationToken cancellationToken = default)
    {
        Logger.Information("Loading next page of authenticated home feed");
        if (!IsSessionActive())
        {
            Logger.Information("No active YouTube session; returning authentication required status");
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

        if (!int.TryParse(currentToken, out var startIndex) || startIndex < 1)
            return new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.Empty, GetHomeFeed(),
                "Invalid recommendation continuation.");

        return await FetchPageAsync(startIndex, false, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _sessionService.SessionChanged -= OnSessionChanged;
    }

    public FeedPage GetHomeFeed()
    {
        lock (_lock)
        {
            return _cachedFeedPage;
        }
    }

    private bool IsSessionActive()
    {
        var session = _sessionService.GetCurrentSession();
        var cookies = _sessionService.GetManualSessionCookies();
        return session is { IsSignedIn: true, HasManualSession: true } && cookies is not null &&
               !string.IsNullOrWhiteSpace(cookies.Content);
    }

    private async Task<AuthenticatedHomeFeedResult> FetchPageAsync(
        int startIndex,
        bool isFirstPage,
        CancellationToken cancellationToken)
    {
        var cookies = _sessionService.GetManualSessionCookies();
        if (cookies is null || string.IsNullOrWhiteSpace(cookies.Content))
        {
            ClearCachedResults();
            return new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.AuthenticationRejected, FeedPage.Empty,
                AuthenticationRejectedMessage);
        }

        using var cookieFile = _cookieFileProvider.CreateCookieFile();
        if (cookieFile is null || string.IsNullOrWhiteSpace(cookieFile.Path))
        {
            ClearCachedResults();
            return new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.AuthenticationRejected, FeedPage.Empty,
                AuthenticationRejectedMessage);
        }

        var executablePath = _preferencesService.GetPreferences().YtDlpExecutablePath;
        var (status, videos, pageEntriesLength, statusMessage) =
            await ExecuteYtDlpAsync(executablePath, cookieFile.Path, startIndex, cancellationToken)
                .ConfigureAwait(false);

        switch (status)
        {
            case AuthenticatedHomeFeedStatus.TemporaryBackendFailure:
                return new AuthenticatedHomeFeedResult(status, FeedPage.Empty, statusMessage);
            case AuthenticatedHomeFeedStatus.Success when videos.Count == 0 && isFirstPage:
            {
                Logger.Information(
                    "Authenticated home feed returned 0 videos; retrying without cookies for public recommendations");
                var retry = await ExecuteYtDlpAsync(executablePath, null, startIndex, cancellationToken)
                    .ConfigureAwait(false);
                if (retry.Status != AuthenticatedHomeFeedStatus.Success)
                    return new AuthenticatedHomeFeedResult(retry.Status, FeedPage.Empty, retry.StatusMessage);

                if (retry.Videos.Count > 0)
                {
                    var retryToken = GetNextContinuationToken(startIndex, retry.PageEntriesLength, PageSize);
                    return CommitVideos(retry.Videos, retryToken, isFirstPage, PublicSuccessMessage);
                }

                break;
            }
            case AuthenticatedHomeFeedStatus.AuthenticationRequired:
            case AuthenticatedHomeFeedStatus.AuthenticationRejected:
            case AuthenticatedHomeFeedStatus.Empty:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (status != AuthenticatedHomeFeedStatus.Success)
            return new AuthenticatedHomeFeedResult(status, FeedPage.Empty, statusMessage);
        var nextToken = GetNextContinuationToken(startIndex, pageEntriesLength, PageSize);
        return CommitVideos(videos, nextToken, isFirstPage, SuccessMessage);
    }

    private AuthenticatedHomeFeedResult CommitVideos(
        IReadOnlyList<VideoSummary> usableVideos,
        string? nextContinuationToken,
        bool isFirstPage,
        string successMessage)
    {
        if (usableVideos.Count == 0)
        {
            if (isFirstPage)
            {
                ClearCachedResults();
                return new AuthenticatedHomeFeedResult(AuthenticatedHomeFeedStatus.Empty, FeedPage.Empty,
                    EmptyFeedMessage);
            }

            lock (_lock)
            {
                _continuationToken = nextContinuationToken;
                _cachedFeedPage = new FeedPage([.. _loadedVideos], _continuationToken);
            }

            return new AuthenticatedHomeFeedResult(
                AuthenticatedHomeFeedStatus.Success,
                new FeedPage(usableVideos, nextContinuationToken),
                successMessage);
        }

        lock (_lock)
        {
            if (isFirstPage)
                _loadedVideos.Clear();

            foreach (var video in usableVideos)
                if (_loadedVideos.All(existing => existing.Id != video.Id))
                    _loadedVideos.Add(video);

            _continuationToken = nextContinuationToken;
            _cachedFeedPage = new FeedPage([.. _loadedVideos], _continuationToken);
        }

        return new AuthenticatedHomeFeedResult(
            AuthenticatedHomeFeedStatus.Success,
            new FeedPage(usableVideos, nextContinuationToken),
            successMessage);
    }

    private async
        Task<(AuthenticatedHomeFeedStatus Status, IReadOnlyList<VideoSummary> Videos, int PageEntriesLength, string
            StatusMessage)> ExecuteYtDlpAsync(
            string executablePath,
            string? cookieFilePath,
            int startIndex,
            CancellationToken cancellationToken)
    {
        ProcessResult processResult;
        try
        {
            processResult = await _runner.RunAsync(
                    YtDlpCommandBuilder.BuildHome(executablePath, startIndex, cookieFilePath),
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
            Logger.Warning(exception, "yt-dlp timed out while loading home recommendations");
            return (AuthenticatedHomeFeedStatus.TemporaryBackendFailure, [], 0,
                RuntimeDependencyGuidance.YtDlpTimedOut);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Could not execute yt-dlp for home recommendations");
            return (AuthenticatedHomeFeedStatus.TemporaryBackendFailure, [], 0,
                RuntimeDependencyGuidance.YtDlpUnavailable(executablePath));
        }

        if (processResult.ExitCode != 0)
        {
            Logger.Warning(
                "yt-dlp exited with code {ExitCode} while loading home recommendations",
                processResult.ExitCode);
            return (AuthenticatedHomeFeedStatus.TemporaryBackendFailure, [], 0,
                RuntimeDependencyGuidance.YtDlpFailed($"the process exited with error code {processResult.ExitCode}."));
        }

        if (string.IsNullOrWhiteSpace(processResult.StandardOutput))
        {
            Logger.Warning("yt-dlp returned empty output for home recommendations");
            return (AuthenticatedHomeFeedStatus.TemporaryBackendFailure, [], 0,
                RuntimeDependencyGuidance.YtDlpFailed("the process returned no output."));
        }

        try
        {
            var pageEntries = YtDlpVideoParser.Parse(processResult.StandardOutput).ToArray();
            var videos = pageEntries
                .Where(video => !video.IsShort)
                .ToArray();
            return (AuthenticatedHomeFeedStatus.Success, videos, pageEntries.Length, SuccessMessage);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Could not parse yt-dlp output for home recommendations");
            return (AuthenticatedHomeFeedStatus.TemporaryBackendFailure, [], 0,
                RuntimeDependencyGuidance.YtDlpFailed("the recommendation output could not be read."));
        }
    }

    private static string? GetNextContinuationToken(int startIndex, int resultCount, int pageSize)
    {
        return resultCount == pageSize
            ? (startIndex + pageSize).ToString(CultureInfo.InvariantCulture)
            : null;
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