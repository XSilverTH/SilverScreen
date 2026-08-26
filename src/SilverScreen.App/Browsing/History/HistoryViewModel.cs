using System.ComponentModel;
using System.Runtime.CompilerServices;
using Serilog;
using SilverScreen.Browsing.Components;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.History;

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

public sealed class HistoryViewModel : INotifyPropertyChanged, IDisposable, IVideoListSource
{
    private static readonly ILogger Logger = Log.ForContext<HistoryViewModel>();
    private readonly PagedFeedEngine _engine;
    private AuthenticatedHistoryStatus _historyStatus = AuthenticatedHistoryStatus.Success;
    private bool _disposed;

    public HistoryViewModel(IAuthenticatedHistoryService historyService)
    {
        ArgumentNullException.ThrowIfNull(historyService);

        _engine = PagedFeedEngine.Create(
            fetchFirstPage: (count, ct) => historyService.LoadFirstPageAsync(count, ct),
            fetchNextPage: (_, count, ct) => historyService.LoadNextPageAsync(count, ct),
            extractResult: res =>
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
            statusMapper: (_, _, state) => HistoryVideoListSource.MapStatus(_historyStatus, state),
            loadingMessage: "Loading watch history…",
            paginationLoadingMessage: "Loading more history…",
            defaultTitle: "History",
            clearOnRefresh: true);

        _engine.EngineStateChanged += OnEngineStateChanged;
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

    public VideoListPresentationState PresentationState => _engine.State;
    VideoListPresentationState IVideoListSource.State => _engine.State;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<HistoryViewState>? StateChanged;
    event EventHandler<VideoListPresentationState>? IVideoListSource.StateChanged
    {
        add => _engine.StateChanged += value;
        remove => _engine.StateChanged -= value;
    }

    public Task LoadAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        ThrowIfDisposed();
        return State.Videos.Count == 0 && !State.IsLoading ? RefreshAsync(count) : Task.CompletedTask;
    }

    public Task RefreshAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        ThrowIfDisposed();
        Logger.Information("HistoryViewModel refreshing watch history");
        return _engine.RefreshAsync(count);
    }

    public Task LoadMoreAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        ThrowIfDisposed();
        return _engine.LoadMoreAsync(count);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine.Dispose();
    }

    private void OnEngineStateChanged(object? sender, FeedEngineState state)
    {
        var summary = state.IsLoading
            ? "Loading watch history…"
            : state.IsLoadingMore
                ? "Loading more watch history…"
                : !state.IsSuccess || state.LastError != null
                    ? (state.IsLoadingMore ? "Could not load more watch history." : "Could not load watch history.")
                    : state.StatusMessage ?? string.Empty;

        var status = state.LastError != null
            ? AuthenticatedHistoryStatus.TemporaryBackendFailure
            : _historyStatus;

        State = new HistoryViewState(
            state.Videos,
            summary,
            state.IsLoading,
            state.IsSuccess && state.LastError == null,
            status,
            state.IsLoadingMore,
            state.HasMore);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
