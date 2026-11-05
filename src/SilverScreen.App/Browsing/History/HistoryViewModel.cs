using Serilog;
using SilverScreen.Browsing.Components;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Common;

namespace SilverScreen.Browsing.History;

public sealed class HistoryViewModel : IVideoListSource
{
    private static readonly ILogger Logger = Log.ForContext<HistoryViewModel>();
    private readonly PagedFeedEngine _engine;
    private readonly Action? _openWebLogin;
    private readonly ISessionService? _sessionService;
    private bool _disposed;

    public HistoryViewModel(
        IAuthenticatedHistoryService historyService,
        ISessionService? sessionService = null,
        Action? openWebLogin = null)
    {
        ArgumentNullException.ThrowIfNull(historyService);
        _sessionService = sessionService;
        _openWebLogin = openWebLogin;


        _engine = PagedFeedEngine.Create(
            historyService.LoadFirstPageAsync,
            (_, count, ct) => historyService.LoadNextPageAsync(count, ct),
            res =>
            {
                var statusMessage = SessionGate.IsAuthInvalid(res.Status)
                    ? SessionGate.SessionNoLongerValidMessage
                    : res.StatusMessage;
                var isSuccess = res.Status is AuthenticatedHistoryStatus.Success or AuthenticatedHistoryStatus.Empty;
                var hasContinuation =
                    res.Status is AuthenticatedHistoryStatus.Success or AuthenticatedHistoryStatus.Empty &&
                    !string.IsNullOrEmpty(res.FeedPage.ContinuationToken);

                return new FeedPageResult(
                    res.FeedPage.Videos,
                    hasContinuation ? res.FeedPage.ContinuationToken : null,
                    isSuccess,
                    statusMessage,
                    AuthenticationStatus: res.Status switch
                    {
                        AuthenticatedHistoryStatus.AuthenticationRequired => FeedAuthenticationStatus.Required,
                        AuthenticatedHistoryStatus.AuthenticationRejected => FeedAuthenticationStatus.Rejected,
                        _ => FeedAuthenticationStatus.None
                    });
            },
            (_, _, state) =>
            {
                var historyStatus = GetHistoryStatus(state);
                return WithSignInAction(
                    MapStatus(historyStatus, state),
                    historyStatus);
            },
            defaultTitle: "History",
            clearOnRefresh: true);


        if (sessionService != null)
            sessionService.SessionChanged += OnSessionChanged;

        if (!IsSessionActive())
            _engine.Reset();
    }

    public bool HasMore => _engine.HasMore;


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_sessionService != null)
            _sessionService.SessionChanged -= OnSessionChanged;
        _engine.Dispose();
    }

    public VideoListPresentationState State => _engine.State;

    public event EventHandler<VideoListPresentationState>? StateChanged
    {
        add => _engine.StateChanged += value;
        remove => _engine.StateChanged -= value;
    }

    public Task RefreshAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        ThrowIfDisposed();
        if (!IsSessionActive())
        {
            GateSignedOut();
            return Task.CompletedTask;
        }

        Logger.Information("HistoryViewModel refreshing watch history");
        return _engine.RefreshAsync(count);
    }

    public Task LoadMoreAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        ThrowIfDisposed();
        return !IsSessionActive() ? Task.CompletedTask : _engine.LoadMoreAsync(count);
    }


    public Task LoadAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        ThrowIfDisposed();
        return State.Videos.Count == 0 && !State.IsLoading ? RefreshAsync(count) : Task.CompletedTask;
    }


    private AuthenticatedHistoryStatus GetHistoryStatus(FeedEngineState state)
    {
        if (state.AuthenticationStatus == FeedAuthenticationStatus.Required)
            return AuthenticatedHistoryStatus.AuthenticationRequired;
        if (state.AuthenticationStatus == FeedAuthenticationStatus.Rejected)
            return AuthenticatedHistoryStatus.AuthenticationRejected;
        if (!IsSessionActive())
            return AuthenticatedHistoryStatus.AuthenticationRequired;
        if (state.LastError != null || !state.IsSuccess)
            return AuthenticatedHistoryStatus.TemporaryBackendFailure;

        if (state.IsLoading || state.IsLoadingMore)
            return AuthenticatedHistoryStatus.Success;

        return state.Videos.Count == 0
            ? AuthenticatedHistoryStatus.Empty
            : AuthenticatedHistoryStatus.Success;
    }


    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (IsSessionActive())
            RefreshAsync().FireAndForget(Logger);
        else
            GateSignedOut();
    }

    private void GateSignedOut()
    {
        _engine.Reset();
    }


    private VideoListStatus WithSignInAction(
        VideoListStatus status,
        AuthenticatedHistoryStatus historyStatus)
    {
        if (!SessionGate.IsAuthInvalid(historyStatus))
            return status;

        return status with
        {
            Description = historyStatus == AuthenticatedHistoryStatus.AuthenticationRejected
                ? SessionGate.SessionNoLongerValidMessage
                : SessionGate.HistorySignedOutMessage,
            ShowRetry = false,
            ActionLabel = _openWebLogin is null
                ? null
                : historyStatus == AuthenticatedHistoryStatus.AuthenticationRejected
                    ? SessionGate.SignInAgainActionLabel
                    : SessionGate.SignInActionLabel,
            Action = _openWebLogin
        };
    }

    private bool IsSessionActive()
    {
        return SessionGate.RequireSignedIn(_sessionService);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public static VideoListStatus MapStatus(AuthenticatedHistoryStatus historyStatus, FeedEngineState state)
    {
        var status = state.LastError != null ? AuthenticatedHistoryStatus.TemporaryBackendFailure : historyStatus;
        var isSuccess = state is { IsSuccess: true, LastError: null };

        return status switch
        {
            AuthenticatedHistoryStatus.AuthenticationRequired or AuthenticatedHistoryStatus.AuthenticationRejected =>
                new VideoListStatus(
                    SessionGate.HistorySignedOutTitle,
                    status == AuthenticatedHistoryStatus.AuthenticationRejected
                        ? SessionGate.SessionNoLongerValidMessage
                        : SessionGate.HistorySignedOutMessage,
                    "avatar-default-symbolic",
                    false,
                    status == AuthenticatedHistoryStatus.AuthenticationRejected
                        ? SessionGate.SignInAgainActionLabel
                        : null),
            AuthenticatedHistoryStatus.TemporaryBackendFailure =>
                new VideoListStatus(
                    SessionGate.HistoryErrorTitle,
                    SessionGate.HistoryErrorMessage,
                    "network-error-symbolic",
                    true),
            _ when !isSuccess =>
                new VideoListStatus(
                    SessionGate.HistoryErrorTitle,
                    SessionGate.HistoryErrorMessage,
                    "network-error-symbolic",
                    true),
            _ => new VideoListStatus(
                SessionGate.HistoryEmptyTitle,
                SessionGate.HistoryEmptyMessage,
                "document-open-recent-symbolic")
        };
    }
}