using System.ComponentModel;
using System.Runtime.CompilerServices;
using Serilog;
using SilverScreen.Browsing.Components;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Infrastructure.Common;

namespace SilverScreen.Browsing.History;

public sealed record HistoryViewState(
    IReadOnlyList<VideoSummary> Videos,
    string Summary,
    bool IsLoading,
    bool IsSuccess,
    AuthenticatedHistoryStatus Status,
    bool IsLoadingMore = false,
    bool HasMore = false)
{
    public static HistoryViewState Empty { get; } = new([], string.Empty, false, true,
        AuthenticatedHistoryStatus.Success);
}

public sealed class HistoryViewModel : INotifyPropertyChanged, IVideoListSource
{
    private static readonly ILogger Logger = Log.ForContext<HistoryViewModel>();
    private readonly PagedFeedEngine _engine;
    private readonly ISessionService? _sessionService;
    private Action? _openWebLogin;
    private bool _disposed;
    private AuthenticatedHistoryStatus _historyStatus = AuthenticatedHistoryStatus.Success;

    public HistoryViewModel(
        IAuthenticatedHistoryService historyService,
        ISessionService? sessionService = null,
        Action? openWebLogin = null)
    {
        ArgumentNullException.ThrowIfNull(historyService);
        _sessionService = sessionService;
        _openWebLogin = openWebLogin;

        if (!IsSessionActive())
            _historyStatus = AuthenticatedHistoryStatus.AuthenticationRequired;

        _engine = PagedFeedEngine.Create(
            historyService.LoadFirstPageAsync,
            (_, count, ct) => historyService.LoadNextPageAsync(count, ct),
            res =>
            {
                _historyStatus = res.Status;
                var isSuccess = res.Status is AuthenticatedHistoryStatus.Success or AuthenticatedHistoryStatus.Empty;
                var hasContinuation = res.Status == AuthenticatedHistoryStatus.Success &&
                                      !string.IsNullOrEmpty(res.FeedPage.ContinuationToken);

                return new FeedPageResult(
                    res.FeedPage.Videos,
                    hasContinuation ? res.FeedPage.ContinuationToken : null,
                    isSuccess,
                    res.StatusMessage);
            },
            (_, _, state) => WithSignInAction(HistoryVideoListSource.MapStatus(_historyStatus, state)),
            "Loading watch history…",
            "Loading more history…",
            defaultTitle: "History",
            clearOnRefresh: true);

        _engine.EngineStateChanged += OnEngineStateChanged;

        if (sessionService != null)
            sessionService.SessionChanged += OnSessionChanged;

        if (!IsSessionActive())
            State = GatedState();
    }

    public HistoryViewState State
    {
        get;
        private set
        {
            field = value;
            OnPropertyChanged();
            StateChanged?.Invoke(this, value);
        }
    } = HistoryViewState.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_sessionService != null)
            _sessionService.SessionChanged -= OnSessionChanged;
        _engine.Dispose();
    }

    VideoListPresentationState IVideoListSource.State => _engine.State;

    event EventHandler<VideoListPresentationState>? IVideoListSource.StateChanged
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
        if (!IsSessionActive())
            return Task.CompletedTask;
        return _engine.LoadMoreAsync(count);
    }

    public IVideoListSource GetVideoListSource(Action? openWebLogin = null)
    {
        if (openWebLogin != null)
            _openWebLogin = openWebLogin;
        if (!IsSessionActive())
            GateSignedOut();
        return this;
    }

    public event EventHandler<HistoryViewState>? StateChanged;

    public Task LoadAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        ThrowIfDisposed();
        return State.Videos.Count == 0 && !State.IsLoading ? RefreshAsync(count) : Task.CompletedTask;
    }

    private void OnEngineStateChanged(object? sender, FeedEngineState state)
    {
        var summary = state.IsLoading
            ? "Loading watch history…"
            : state.IsLoadingMore
                ? "Loading more watch history…"
                : !state.IsSuccess || state.LastError != null
                    ? state.IsLoadingMore ? "Could not load more watch history." : "Could not load watch history."
                    : state.StatusMessage ?? string.Empty;

        var status = state.LastError != null
            ? AuthenticatedHistoryStatus.TemporaryBackendFailure
            : _historyStatus;

        State = new HistoryViewState(
            state.Videos,
            summary,
            state.IsLoading,
            state is { IsSuccess: true, LastError: null },
            status,
            state.IsLoadingMore,
            state.HasMore);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (IsSessionActive())
        {
            RefreshAsync().FireAndForget(Logger);
        }
        else
        {
            GateSignedOut();
        }
    }

    private void GateSignedOut()
    {
        _historyStatus = AuthenticatedHistoryStatus.AuthenticationRequired;
        _engine.Reset();
        State = GatedState();
    }

    private static HistoryViewState GatedState()
    {
        return new HistoryViewState(
            [],
            "Sign in with Google or use cookies.txt to see your watch history.",
            false,
            false,
            AuthenticatedHistoryStatus.AuthenticationRequired);
    }

    private VideoListStatus WithSignInAction(VideoListStatus status)
    {
        if (_historyStatus is not (AuthenticatedHistoryStatus.AuthenticationRequired
            or AuthenticatedHistoryStatus.AuthenticationRejected))
            return status;

        return status with
        {
            Description = "Sign in with Google or use cookies.txt to see your watch history.",
            ShowRetry = false,
            ActionLabel = _openWebLogin is null ? null : "Sign In",
            Action = _openWebLogin
        };
    }

    private bool IsSessionActive()
    {
        if (_sessionService is null)
            return true;
        var session = _sessionService.GetCurrentSession();
        var cookies = _sessionService.GetManualSessionCookies();
        return session is { IsSignedIn: true, HasManualSession: true } && cookies != null &&
               !string.IsNullOrWhiteSpace(cookies.Content);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}