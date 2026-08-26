using Serilog;
using SilverScreen.Browsing.Components;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Infrastructure.Common;

namespace SilverScreen.Browsing.Home;

public sealed class HomeFeedCoordinator : IDisposable, IVideoListSource
{
    private static readonly ILogger Logger = Log.ForContext<HomeFeedCoordinator>();
    private readonly ISessionService _sessionService;
    private readonly PagedFeedEngine _engine;
    private readonly Lock _lock = new();
    private AuthenticatedHomeFeedStatus _lastStatus = AuthenticatedHomeFeedStatus.Success;
    private bool _disposed;

    public HomeFeedCoordinator(ISessionService sessionService, IAuthenticatedHomeFeedService feedService)
    {
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        ArgumentNullException.ThrowIfNull(feedService);

        _engine = PagedFeedEngine.Create(
            fetchFirstPage: (count, ct) => feedService.LoadFirstPageAsync(count, ct),
            fetchNextPage: (_, count, ct) => feedService.LoadNextPageAsync(count, ct),
            extractResult: res =>
            {
                lock (_lock)
                {
                    _lastStatus = res.Status;
                }

                if (res.Status is AuthenticatedHomeFeedStatus.AuthenticationRequired or AuthenticatedHomeFeedStatus.AuthenticationRejected)
                {
                    return FeedPageResult.Failed("Your YouTube session is no longer valid.", clearExisting: true);
                }

                var isSuccess = res.Status is AuthenticatedHomeFeedStatus.Success or AuthenticatedHomeFeedStatus.Empty;
                var hasContinuation = res.Status == AuthenticatedHomeFeedStatus.Success &&
                                      !string.IsNullOrEmpty(res.FeedPage.ContinuationToken);

                if (!isSuccess)
                {
                    return FeedPageResult.Failed("Could not load YouTube recommendations.", clearExisting: false);
                }

                return new FeedPageResult(
                    res.FeedPage.Videos,
                    hasContinuation ? res.FeedPage.ContinuationToken : null,
                    isSuccess,
                    res.StatusMessage);
            },
            statusMapper: (_, _, state) => MapHomeStatus(state),
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
    public VideoListPresentationState PresentationState => _engine.State;
    VideoListPresentationState IVideoListSource.State => _engine.State;

    public event EventHandler<HomeFeedState>? StateChanged;
    event EventHandler<VideoListPresentationState>? IVideoListSource.StateChanged
    {
        add => _engine.StateChanged += value;
        remove => _engine.StateChanged -= value;
    }

    public Task RefreshAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        Logger.Information("HomeFeedCoordinator refreshing home feed");
        if (!IsSessionActive())
        {
            _engine.Reset(status: MapSignedOutStatus());
            UpdateHomeFeedState(HomeFeedState.SignedOut);
            return Task.CompletedTask;
        }

        return _engine.RefreshAsync(count);
    }

    public Task LoadMoreAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        Logger.Information("HomeFeedCoordinator loading more home feed items");
        if (!IsSessionActive())
            return Task.CompletedTask;

        return _engine.LoadMoreAsync(count);
    }

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

    private void OnEngineStateChanged(object? sender, FeedEngineState engineState)
    {
        if (!IsSessionActive())
        {
            UpdateHomeFeedState(HomeFeedState.SignedOut);
            return;
        }

        HomeFeedStateKind kind;
        string? message = engineState.StatusMessage;

        AuthenticatedHomeFeedStatus lastStatus;
        lock (_lock)
        {
            lastStatus = _lastStatus;
        }

        if (lastStatus is AuthenticatedHomeFeedStatus.AuthenticationRequired or AuthenticatedHomeFeedStatus.AuthenticationRejected)
        {
            kind = HomeFeedStateKind.AuthenticationRequired;
            message = "Your YouTube session is no longer valid.";
        }
        else if (engineState.LastError != null || !engineState.IsSuccess)
        {
            kind = HomeFeedStateKind.SafeError;
            message = "Could not load YouTube recommendations.";
        }
        else if (engineState.IsLoading && engineState.Videos.Count == 0)
        {
            kind = HomeFeedStateKind.InitialLoading;
        }
        else if (engineState.Videos.Count == 0 && !engineState.IsLoading)
        {
            kind = HomeFeedStateKind.Empty;
            message = "No recommendations are available right now.";
        }
        else
        {
            kind = HomeFeedStateKind.Ready;
        }

        var newState = new HomeFeedState(
            kind,
            engineState.Videos.ToArray(),
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
            _engine.Reset(status: MapSignedOutStatus());
            UpdateHomeFeedState(HomeFeedState.SignedOut);
        }
    }

    private static VideoListStatus MapSignedOutStatus() => new(
        "Home",
        "Sign in to see your YouTube recommendations.",
        "avatar-default-symbolic");

    private static VideoListStatus MapHomeStatus(FeedEngineState state)
    {
        if (state.LastError != null || !state.IsSuccess)
        {
            return new VideoListStatus(
                "Home",
                "Could not load YouTube recommendations.",
                "network-error-symbolic");
        }

        if (state.Videos.Count == 0 && !state.IsLoading)
        {
            return new VideoListStatus(
                "Home",
                "No recommendations are available right now.",
                "applications-internet-symbolic");
        }

        return new VideoListStatus(
            "Home",
            "No recommendations are available right now.",
            "applications-internet-symbolic");
    }
}
