using Serilog;
using SilverScreen.Browsing.Components;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Core.Common;
using SilverScreen.Features;

namespace SilverScreen.Browsing.Home;

public sealed class HomeFeedCoordinator : IVideoListSource
{
    private static readonly ILogger Logger = Log.ForContext<HomeFeedCoordinator>();
    private readonly PagedFeedEngine _engine;
    private readonly Lock _lock = new();
    private readonly ISessionService _sessionService;
    private bool _disposed;
    private AuthenticatedHomeFeedStatus _lastStatus = AuthenticatedHomeFeedStatus.Success;
    private Action? _openWebLogin;

    public HomeFeedCoordinator(ISessionService sessionService, IAuthenticatedHomeFeedService feedService,
        Action? openWebLogin = null)
    {
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _openWebLogin = openWebLogin;
        ArgumentNullException.ThrowIfNull(feedService);

        _engine = PagedFeedEngine.Create(
            feedService.LoadFirstPageAsync,
            (_, count, ct) => feedService.LoadNextPageAsync(count, ct),
            res =>
            {
                lock (_lock)
                {
                    _lastStatus = res.Status;
                }

                if (SessionGate.IsAuthInvalid(res.Status))
                    return FeedPageResult.Failed(SessionGate.SessionNoLongerValidMessage, true);

                var isSuccess = res.Status is AuthenticatedHomeFeedStatus.Success or AuthenticatedHomeFeedStatus.Empty;
                var hasContinuation = res.Status == AuthenticatedHomeFeedStatus.Success &&
                                      !string.IsNullOrEmpty(res.FeedPage.ContinuationToken);

                if (!isSuccess) return FeedPageResult.Failed(SessionGate.HomeLoadErrorMessage);

                return new FeedPageResult(
                    res.FeedPage.Videos,
                    hasContinuation ? res.FeedPage.ContinuationToken : null,
                    isSuccess,
                    res.StatusMessage);
            },
            (_, _, state) => MapHomeStatus(state),
            defaultTitle: "Home",
            clearOnRefresh: false);

        _engine.EngineStateChanged += OnEngineStateChanged;
        _sessionService.SessionChanged += OnSessionChanged;

        if (IsSessionActive())
        {
            State = new HomeFeedState(HomeFeedStateKind.InitialLoading, [], IsLoading: true);
            RefreshAsync().FireAndForget(Logger);
        }
        else
        {
            State = HomeFeedState.SignedOut;
            _engine.SetStatus(MapSignedOutStatus());
        }
    }

    public HomeFeedState State { get; private set; }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            _sessionService.SessionChanged -= OnSessionChanged;
            _engine.Dispose();
        }
    }

    VideoListPresentationState IVideoListSource.State => _engine.State;

    event EventHandler<VideoListPresentationState>? IVideoListSource.StateChanged
    {
        add => _engine.StateChanged += value;
        remove => _engine.StateChanged -= value;
    }

    public Task RefreshAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        Logger.Information("HomeFeedCoordinator refreshing home feed");
        if (IsSessionActive()) return _engine.RefreshAsync(count);
        _engine.Reset(MapSignedOutStatus());
        UpdateHomeFeedState(HomeFeedState.SignedOut);
        return Task.CompletedTask;
    }

    public Task LoadMoreAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        if (!IsSessionActive() || _engine.IsLoading || _engine.IsLoadingMore || !_engine.HasMore)
            return Task.CompletedTask;

        Logger.Information("HomeFeedCoordinator loading more home feed items");
        return _engine.LoadMoreAsync(count);
    }

    public IVideoListSource GetVideoListSource(Action? openWebLogin = null)
    {
        if (openWebLogin != null)
            _openWebLogin = openWebLogin;
        if (!IsSessionActive())
            _engine.Reset(MapSignedOutStatus());
        return this;
    }


    public event EventHandler<HomeFeedState>? StateChanged;

    private void OnEngineStateChanged(object? sender, FeedEngineState engineState)
    {
        if (!IsSessionActive())
        {
            UpdateHomeFeedState(HomeFeedState.SignedOut);
            return;
        }

        AuthenticatedHomeFeedStatus lastStatus;
        lock (_lock)
        {
            lastStatus = _lastStatus;
        }

        var (kind, message) = SessionGate.MapHomeFeedOutcome(lastStatus, engineState);
        var newState = new HomeFeedState(
            kind,
            [.. engineState.Videos],
            message,
            engineState.IsLoading,
            engineState.IsLoadingMore,
            engineState.HasMore);

        UpdateHomeFeedState(newState);
    }

    private void UpdateHomeFeedState(HomeFeedState newState)
    {
        lock (_lock)
        {
            State = newState;
        }

        StateChanged?.Invoke(this, newState);
    }

    private bool IsSessionActive()
    {
        return SessionGate.RequireSignedIn(_sessionService);
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (IsSessionActive())
        {
            RefreshAsync().FireAndForget(Logger);
        }
        else
        {
            _engine.Reset(MapSignedOutStatus());
            UpdateHomeFeedState(HomeFeedState.SignedOut);
        }
    }

    private VideoListStatus MapSignedOutStatus()
    {
        return SessionGate.HomeSignedOutStatus(_openWebLogin);
    }

    private VideoListStatus MapHomeStatus(FeedEngineState state)
    {
        AuthenticatedHomeFeedStatus lastStatus;
        lock (_lock)
        {
            lastStatus = _lastStatus;
        }

        if (SessionGate.IsAuthInvalid(lastStatus))
            return SessionGate.HomeAuthInvalidStatus(_openWebLogin);

        if (state.LastError != null || !state.IsSuccess)
            return SessionGate.HomeErrorStatus();

        return SessionGate.HomeEmptyStatus();
    }
}