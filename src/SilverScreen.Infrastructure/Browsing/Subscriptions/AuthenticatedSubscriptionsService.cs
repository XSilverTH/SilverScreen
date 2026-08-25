using System.Globalization;
using Serilog;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Subscriptions;
using SilverScreen.Core.Common;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Infrastructure.Browsing.Subscriptions;

/// <summary>Keeps the current server-backed subscription feed and channels sequence for the active session.</summary>
public sealed class AuthenticatedSubscriptionsService : IAuthenticatedSubscriptionsService, IDisposable
{
    private const string AuthenticationRequiredMessage = "Sign in to YouTube to load your subscriptions.";
    private const string AuthenticationRejectedMessage = "The YouTube session was rejected or has expired.";
    private const string EmptySubscriptionsMessage = "No subscription videos were returned.";
    private const string NoContinuationMessage = "No additional subscription videos are available.";
    private const string SuccessMessage = "Subscriptions loaded.";
    private const string ChannelsSuccessMessage = "Subscribed channels loaded.";
    private const string ChannelsEmptyMessage = "No subscribed channels were returned.";

    private static readonly ILogger Logger = Log.ForContext<AuthenticatedSubscriptionsService>();
    private readonly ICookieFileProvider _cookieFileProvider;
    private readonly List<VideoSummary> _loadedVideos = [];
    private readonly List<SubscribedChannel> _loadedChannels = [];
    private readonly Lock _lock = new();
    private readonly IPreferencesService _preferencesService;
    private readonly IYtDlpRunner _runner;
    private readonly ISessionService _sessionService;
    private readonly TimeSpan _timeout;
    private string? _continuationToken;

    public AuthenticatedSubscriptionsService(
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

    public async Task<AuthenticatedSubscriptionsFeedResult> LoadFirstFeedPageAsync(
        int count = VideoFeedConstants.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!IsSessionActive())
            return new AuthenticatedSubscriptionsFeedResult(
                AuthenticatedSubscriptionsStatus.AuthenticationRequired,
                FeedPage.Empty,
                AuthenticationRequiredMessage);

        return await FetchFeedPageAsync(1, count, isFirstPage: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AuthenticatedSubscriptionsFeedResult> LoadNextFeedPageAsync(
        int count = VideoFeedConstants.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!IsSessionActive())
            return new AuthenticatedSubscriptionsFeedResult(
                AuthenticatedSubscriptionsStatus.AuthenticationRequired,
                FeedPage.Empty,
                AuthenticationRequiredMessage);

        int startIndex;
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(_continuationToken) ||
                !int.TryParse(_continuationToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out startIndex))
                return new AuthenticatedSubscriptionsFeedResult(
                    AuthenticatedSubscriptionsStatus.Empty,
                    GetFeed(),
                    NoContinuationMessage);
        }

        return await FetchFeedPageAsync(startIndex, count, isFirstPage: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SubscribedChannelsResult> LoadSubscribedChannelsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsSessionActive())
            return new SubscribedChannelsResult(
                AuthenticatedSubscriptionsStatus.AuthenticationRequired,
                [],
                AuthenticationRequiredMessage);

        var cookies = _sessionService.GetManualSessionCookies();
        if (cookies is null || string.IsNullOrWhiteSpace(cookies.Content))
        {
            ClearCachedResults();
            return new SubscribedChannelsResult(
                AuthenticatedSubscriptionsStatus.AuthenticationRejected,
                [],
                AuthenticationRejectedMessage);
        }

        using var cookieFile = _cookieFileProvider.CreateCookieFile();
        if (cookieFile is null || string.IsNullOrWhiteSpace(cookieFile.Path))
        {
            ClearCachedResults();
            return new SubscribedChannelsResult(
                AuthenticatedSubscriptionsStatus.AuthenticationRejected,
                [],
                AuthenticationRejectedMessage);
        }

        var executablePath = _preferencesService.GetPreferences().YtDlpExecutablePath;
        ProcessResult processResult;
        try
        {
            processResult = await _runner.RunAsync(
                    YtDlpCommandBuilder.BuildSubscribedChannels(executablePath, cookieFile.Path),
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
            Logger.Warning(exception, "yt-dlp timed out while loading subscribed channels");
            return new SubscribedChannelsResult(
                AuthenticatedSubscriptionsStatus.TemporaryBackendFailure,
                [],
                RuntimeDependencyGuidance.YtDlpTimedOut);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Could not execute yt-dlp for subscribed channels");
            return new SubscribedChannelsResult(
                AuthenticatedSubscriptionsStatus.TemporaryBackendFailure,
                [],
                RuntimeDependencyGuidance.YtDlpUnavailable(executablePath));
        }

        if (processResult.ExitCode != 0)
        {
            Logger.Warning("yt-dlp exited with code {ExitCode} while loading subscribed channels", processResult.ExitCode);
            return new SubscribedChannelsResult(
                AuthenticatedSubscriptionsStatus.TemporaryBackendFailure,
                [],
                RuntimeDependencyGuidance.YtDlpFailed($"the process exited with error code {processResult.ExitCode}."));
        }

        if (string.IsNullOrWhiteSpace(processResult.StandardOutput))
        {
            lock (_lock)
            {
                _loadedChannels.Clear();
            }

            return new SubscribedChannelsResult(
                AuthenticatedSubscriptionsStatus.Empty,
                [],
                ChannelsEmptyMessage);
        }

        try
        {
            var channels = YtDlpSubscriptionsParser.ParseChannels(processResult.StandardOutput);

            lock (_lock)
            {
                _loadedChannels.Clear();
                _loadedChannels.AddRange(channels);
            }

            return new SubscribedChannelsResult(
                channels.Count > 0 ? AuthenticatedSubscriptionsStatus.Success : AuthenticatedSubscriptionsStatus.Empty,
                channels,
                channels.Count > 0 ? ChannelsSuccessMessage : ChannelsEmptyMessage);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Could not parse yt-dlp output for subscribed channels");
            return new SubscribedChannelsResult(
                AuthenticatedSubscriptionsStatus.TemporaryBackendFailure,
                [],
                RuntimeDependencyGuidance.YtDlpFailed("the subscribed channels output could not be read."));
        }
    }

    public void Dispose()
    {
        _sessionService.SessionChanged -= OnSessionChanged;
    }

    private FeedPage GetFeed()
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
        return session is { IsSignedIn: true, HasManualSession: true } && cookies != null &&
               !string.IsNullOrWhiteSpace(cookies.Content);
    }

    private async Task<AuthenticatedSubscriptionsFeedResult> FetchFeedPageAsync(
        int startIndex,
        int count,
        bool isFirstPage,
        CancellationToken cancellationToken)
    {
        var cookies = _sessionService.GetManualSessionCookies();
        if (cookies is null || string.IsNullOrWhiteSpace(cookies.Content))
        {
            ClearCachedResults();
            return new AuthenticatedSubscriptionsFeedResult(
                AuthenticatedSubscriptionsStatus.AuthenticationRejected,
                FeedPage.Empty,
                AuthenticationRejectedMessage);
        }

        using var cookieFile = _cookieFileProvider.CreateCookieFile();
        if (cookieFile is null || string.IsNullOrWhiteSpace(cookieFile.Path))
        {
            ClearCachedResults();
            return new AuthenticatedSubscriptionsFeedResult(
                AuthenticatedSubscriptionsStatus.AuthenticationRejected,
                FeedPage.Empty,
                AuthenticationRejectedMessage);
        }

        var executablePath = _preferencesService.GetPreferences().YtDlpExecutablePath;
        ProcessResult processResult;
        try
        {
            processResult = await _runner.RunAsync(
                    YtDlpCommandBuilder.BuildSubscriptions(executablePath, startIndex, count, cookieFile.Path),
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
            Logger.Warning(exception, "yt-dlp timed out while loading subscription feed");
            return new AuthenticatedSubscriptionsFeedResult(
                AuthenticatedSubscriptionsStatus.TemporaryBackendFailure,
                FeedPage.Empty,
                RuntimeDependencyGuidance.YtDlpTimedOut);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Could not execute yt-dlp for subscription feed");
            return new AuthenticatedSubscriptionsFeedResult(
                AuthenticatedSubscriptionsStatus.TemporaryBackendFailure,
                FeedPage.Empty,
                RuntimeDependencyGuidance.YtDlpUnavailable(executablePath));
        }

        if (processResult.ExitCode != 0)
        {
            Logger.Warning("yt-dlp exited with code {ExitCode} while loading subscription feed", processResult.ExitCode);
            return new AuthenticatedSubscriptionsFeedResult(
                AuthenticatedSubscriptionsStatus.TemporaryBackendFailure,
                FeedPage.Empty,
                RuntimeDependencyGuidance.YtDlpFailed($"the process exited with error code {processResult.ExitCode}."));
        }

        if (string.IsNullOrWhiteSpace(processResult.StandardOutput))
        {
            if (!isFirstPage)
                return new AuthenticatedSubscriptionsFeedResult(
                    AuthenticatedSubscriptionsStatus.Empty,
                    GetFeed(),
                    NoContinuationMessage);

            ClearCachedFeed();
            return new AuthenticatedSubscriptionsFeedResult(
                AuthenticatedSubscriptionsStatus.Empty,
                FeedPage.Empty,
                EmptySubscriptionsMessage);
        }

        try
        {
            var pageEntries = YtDlpVideoParser.Parse(processResult.StandardOutput).ToArray();
            var usableVideos = pageEntries.Where(video => !video.IsShort).ToArray();

            if (usableVideos.Length == 0 && isFirstPage)
            {
                ClearCachedFeed();
                return new AuthenticatedSubscriptionsFeedResult(
                    AuthenticatedSubscriptionsStatus.Empty,
                    FeedPage.Empty,
                    EmptySubscriptionsMessage);
            }

            var nextToken = pageEntries.Length == count
                ? (startIndex + count).ToString(CultureInfo.InvariantCulture)
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

            return new AuthenticatedSubscriptionsFeedResult(
                AuthenticatedSubscriptionsStatus.Success,
                new FeedPage(usableVideos, nextToken),
                SuccessMessage);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Could not parse yt-dlp output for subscription feed");
            return new AuthenticatedSubscriptionsFeedResult(
                AuthenticatedSubscriptionsStatus.TemporaryBackendFailure,
                FeedPage.Empty,
                RuntimeDependencyGuidance.YtDlpFailed("the subscription feed output could not be read."));
        }
    }

    private void ClearCachedFeed()
    {
        lock (_lock)
        {
            _loadedVideos.Clear();
            _continuationToken = null;
        }
    }

    private void ClearCachedResults()
    {
        lock (_lock)
        {
            _loadedVideos.Clear();
            _loadedChannels.Clear();
            _continuationToken = null;
        }
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        ClearCachedResults();
    }
}
