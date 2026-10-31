using SilverScreen.Core.Browsing.Home;
using Serilog;
using SilverScreen.Browsing.Components;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Common;
using SilverScreen.Core.Account.Session;

namespace SilverScreen.Browsing.Home;

public sealed class HomeFeedCoordinator : IVideoListSource
{
    private static readonly ILogger Logger = Log.ForContext<HomeFeedCoordinator>();
    private readonly PagedFeedEngine _engine;
    private readonly Lock _lock = new();
    private readonly ISessionService _sessionService;
    private bool _disposed;
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
                if (SessionGate.IsAuthInvalid(res.Status))
                    return FeedPageResult.Failed(
                        SessionGate.SessionNoLongerValidMessage,
                        clearExisting: true,
                        authenticationStatus: res.Status == AuthenticatedHomeFeedStatus.AuthenticationRejected
                            ? FeedAuthenticationStatus.Rejected
                            : FeedAuthenticationStatus.Required);
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

        _sessionService.SessionChanged += OnSessionChanged;

        if (IsSessionActive())
        {
            RefreshAsync().FireAndForget(Logger);
        }
        else
        {
            _engine.SetStatus(MapSignedOutStatus());
        }
    }

    public VideoListPresentationState State => _engine.State;

    public bool HasMore => _engine.HasMore;

    public event EventHandler<VideoListPresentationState>? StateChanged
    {
        add => _engine.StateChanged += value;
        remove => _engine.StateChanged -= value;
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


    public Task RefreshAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        Logger.Information("HomeFeedCoordinator refreshing home feed");
        if (IsSessionActive()) return _engine.RefreshAsync(count);
        _engine.Reset(MapSignedOutStatus());
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
        }
    }

    private VideoListStatus MapSignedOutStatus()
    {
        return HomeSignedOutStatus(_openWebLogin);
    }

    private VideoListStatus MapHomeStatus(FeedEngineState state)
    {
        if (state.AuthenticationStatus is FeedAuthenticationStatus.Required or FeedAuthenticationStatus.Rejected)
            return HomeAuthInvalidStatus(
                _openWebLogin,
                state.AuthenticationStatus == FeedAuthenticationStatus.Rejected);

        if (state.LastError != null || !state.IsSuccess)
            return HomeErrorStatus();

        return HomeEmptyStatus();
    }

    internal static VideoListStatus HomeSignedOutStatus(Action? openWebLogin)
    {
        return new VideoListStatus(
            "Home",
            SessionGate.HomeSignedOutMessage,
            "avatar-default-symbolic",
            false,
            SessionGate.SignInActionLabel,
            openWebLogin);
    }

    internal static VideoListStatus HomeAuthInvalidStatus(Action? openWebLogin, bool authenticationRejected = true)
    {
        return new VideoListStatus(
            "Home",
            authenticationRejected ? SessionGate.SessionNoLongerValidMessage : SessionGate.HomeSignedOutMessage,
            authenticationRejected ? "dialog-password-symbolic" : "avatar-default-symbolic",
            false,
            authenticationRejected
                ? SessionGate.SignInAgainActionLabel
                : SessionGate.SignInActionLabel,
            openWebLogin);
    }

    internal static VideoListStatus HomeErrorStatus()
    {
        return new VideoListStatus(
            "Home",
            SessionGate.HomeLoadErrorMessage,
            "network-error-symbolic",
            true);
    }

    internal static VideoListStatus HomeEmptyStatus()
    {
        return new VideoListStatus(
            "Home",
            SessionGate.HomeEmptyMessage,
            "applications-internet-symbolic");
    }

}
