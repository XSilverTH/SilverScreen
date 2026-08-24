using SilverScreen.Infrastructure.Common;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Search;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Queue;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure;

namespace SilverScreen.Browsing.Home;

public sealed class HomeFeedCoordinator : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<HomeFeedCoordinator>();
    private readonly IAuthenticatedHomeFeedService _feedService;
    private readonly Lock _lock = new();
    private readonly ISessionService _sessionService;
    private readonly List<VideoSummary> _videos = [];
    private string? _continuationToken;
    private CancellationTokenSource? _cts;

    private long _currentRequestId;
    private bool _isLoading;
    private long _publishedStateVersion;
    private long _stateVersion;

    public HomeFeedCoordinator(ISessionService sessionService, IAuthenticatedHomeFeedService feedService)
    {
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _feedService = feedService ?? throw new ArgumentNullException(nameof(feedService));

        _sessionService.SessionChanged += OnSessionChanged;

        if (IsSessionActive())
        {
            State = new HomeFeedState(HomeFeedStateKind.InitialLoading, [], IsLoading: true);
            RefreshAsync().FireAndForget(Logger);
        }
        else
        {
            State = HomeFeedState.SignedOut;
        }
    }

    public HomeFeedState State { get; private set; }

    public void Dispose()
    {
        _sessionService.SessionChanged -= OnSessionChanged;
        CancelAndClear();
    }

    public event EventHandler<HomeFeedState>? StateChanged;

    public async Task RefreshAsync()
    {
        Logger.Information("HomeFeedCoordinator refreshing home feed");
        if (!IsSessionActive())
        {
            CancelAndClear();
            PublishState(HomeFeedState.SignedOut);
            return;
        }

        await ExecuteFeedRequestAsync(
            token => _feedService.LoadFirstPageAsync(token),
            true,
            "HomeFeedCoordinator failed to refresh home feed");
    }

    public async Task LoadMoreAsync()
    {
        Logger.Information("HomeFeedCoordinator loading more home feed items");
        await ExecuteFeedRequestAsync(
            token => _feedService.LoadNextPageAsync(token),
            false,
            "HomeFeedCoordinator failed to load more recommendations",
            () => !_isLoading && IsSessionActive() && !string.IsNullOrEmpty(_continuationToken));
    }

    private async Task ExecuteFeedRequestAsync(
        Func<CancellationToken, Task<AuthenticatedHomeFeedResult>> fetchDelegate,
        bool isFirstPage,
        string failureLogMessage,
        Func<bool>? guard = null)
    {
        CancellationToken token;
        long requestId;
        lock (_lock)
        {
            if (guard != null && !guard())
                return;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            token = _cts.Token;
            _isLoading = true;
            _currentRequestId++;
            requestId = _currentRequestId;
        }

        HomeFeedState pendingState;
        long version;
        lock (_lock)
        {
            if (_currentRequestId != requestId)
                return;

            if (isFirstPage)
                pendingState = _videos.Count > 0
                    ? new HomeFeedState(
                        HomeFeedStateKind.Ready,
                        [.. _videos],
                        IsLoading: true,
                        HasContinuation: !string.IsNullOrEmpty(_continuationToken))
                    : new HomeFeedState(
                        HomeFeedStateKind.InitialLoading,
                        [],
                        IsLoading: true);
            else
                pendingState = new HomeFeedState(
                    HomeFeedStateKind.Ready,
                    [.. _videos],
                    IsLoadingMore: true,
                    HasContinuation: !string.IsNullOrEmpty(_continuationToken));

            _stateVersion++;
            State = pendingState;
            version = _stateVersion;
        }

        PublishStateWithVersion(pendingState, version);

        try
        {
            var result = await fetchDelegate(token);

            token.ThrowIfCancellationRequested();

            ProcessResult(result, isFirstPage, requestId);
        }
        catch (OperationCanceledException)
        {
            // Cancellation never publishes errors
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "{FailureReason}", failureLogMessage);
            HomeFeedState errorState;
            long errVersion;
            lock (_lock)
            {
                if (_currentRequestId == requestId)
                {
                    errorState = new HomeFeedState(
                        HomeFeedStateKind.SafeError,
                        [.. _videos],
                        "Could not load YouTube recommendations.",
                        false,
                        false,
                        !string.IsNullOrEmpty(_continuationToken));

                    _stateVersion++;
                    State = errorState;
                    errVersion = _stateVersion;
                }
                else
                {
                    return;
                }
            }

            PublishStateWithVersion(errorState, errVersion);
        }
        finally
        {
            lock (_lock)
            {
                if (_currentRequestId == requestId)
                    _isLoading = false;
            }
        }
    }

    private void ProcessResult(AuthenticatedHomeFeedResult result, bool isFirstPage, long requestId)
    {
        HomeFeedState nextState;
        long version;

        lock (_lock)
        {
            if (_currentRequestId != requestId)
                return;

            switch (result.Status)
            {
                case AuthenticatedHomeFeedStatus.AuthenticationRequired:
                case AuthenticatedHomeFeedStatus.AuthenticationRejected:
                    _videos.Clear();
                    _continuationToken = null;
                    nextState = new HomeFeedState(
                        HomeFeedStateKind.AuthenticationRequired,
                        [],
                        "Your YouTube session is no longer valid.");
                    break;
                case AuthenticatedHomeFeedStatus.TemporaryBackendFailure:
                    nextState = new HomeFeedState(
                        HomeFeedStateKind.SafeError,
                        [.. _videos],
                        "Could not load YouTube recommendations.",
                        false,
                        false,
                        !string.IsNullOrEmpty(_continuationToken));
                    break;
                case AuthenticatedHomeFeedStatus.Empty when isFirstPage:
                    _videos.Clear();
                    _continuationToken = null;
                    nextState = new HomeFeedState(
                        HomeFeedStateKind.Empty,
                        [],
                        "No recommendations are available right now.");
                    break;
                case AuthenticatedHomeFeedStatus.Empty:
                    _continuationToken = null;
                    nextState = new HomeFeedState(
                        HomeFeedStateKind.Ready,
                        [.. _videos],
                        IsLoading: false,
                        IsLoadingMore: false,
                        HasContinuation: false);
                    break;
                // Success
                case AuthenticatedHomeFeedStatus.Success:
                default:
                {
                    var newVideos = result.FeedPage.Videos
                        .Where(v => !v.IsShort)
                        .ToList();

                    if (isFirstPage) _videos.Clear();

                    foreach (var video in newVideos.Where(video => _videos.All(existing => existing.Id != video.Id)))
                        _videos.Add(video);

                    _continuationToken = result.FeedPage.ContinuationToken;

                    if (_videos.Count == 0)
                        nextState = new HomeFeedState(
                            HomeFeedStateKind.Empty,
                            [],
                            "No recommendations are available right now.");
                    else
                        nextState = new HomeFeedState(
                            HomeFeedStateKind.Ready,
                            [.. _videos],
                            IsLoading: false,
                            IsLoadingMore: false,
                            HasContinuation: !string.IsNullOrEmpty(_continuationToken));

                    break;
                }
            }

            _stateVersion++;
            State = nextState;
            version = _stateVersion;
        }

        PublishStateWithVersion(nextState, version);
    }

    private bool IsSessionActive()
    {
        var session = _sessionService.GetCurrentSession();
        var cookies = _sessionService.GetManualSessionCookies();
        return session is { IsSignedIn: true, HasManualSession: true } && cookies != null &&
               !string.IsNullOrWhiteSpace(cookies.Content);
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (IsSessionActive())
        {
            RefreshAsync().FireAndForget(Logger);
        }
        else
        {
            CancelAndClear();
            PublishState(HomeFeedState.SignedOut);
        }
    }

    private void CancelAndClear()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _isLoading = false;
            _videos.Clear();
            _continuationToken = null;
            _currentRequestId++;
        }
    }

    private void PublishState(HomeFeedState newState)
    {
        long version;
        lock (_lock)
        {
            _stateVersion++;
            State = newState;
            version = _stateVersion;
        }

        PublishStateWithVersion(newState, version);
    }

    private void PublishStateWithVersion(HomeFeedState stateToPublish, long version)
    {
        var shouldPublish = false;
        lock (_lock)
        {
            if (version == _stateVersion && version > _publishedStateVersion)
            {
                _publishedStateVersion = version;
                shouldPublish = true;
            }
        }

        if (shouldPublish)
            StateChanged?.Invoke(this, stateToPublish);
    }
}