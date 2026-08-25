using System.ComponentModel;
using System.Runtime.CompilerServices;
using Serilog;
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

public sealed class HistoryViewModel(IAuthenticatedHistoryService historyService) : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<HistoryViewModel>();
    private bool _disposed;
    private CancellationTokenSource? _requestCancellation;
    private long _requestGeneration;
    private int _lastRequestedCount = VideoFeedConstants.DefaultPageSize;
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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ++_requestGeneration;
        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();
        _requestCancellation = null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<HistoryViewState>? StateChanged;

    public Task LoadAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        _lastRequestedCount = count;
        return State.Videos.Count == 0 && !State.IsLoading ? RefreshAsync(count) : Task.CompletedTask;
    }

    public async Task RefreshAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        Logger.Information("HistoryViewModel refreshing watch history");
        ThrowIfDisposed();
        _lastRequestedCount = count;
        _requestCancellation?.Dispose();
        _requestCancellation = new CancellationTokenSource();
        var token = _requestCancellation.Token;
        var generation = ++_requestGeneration;
        State = new HistoryViewState([], "Loading watch history…", true, true, AuthenticatedHistoryStatus.Success);
        try
        {
            var result = await historyService.LoadFirstPageAsync(count, token).ConfigureAwait(false);
            if (token.IsCancellationRequested || generation != _requestGeneration || _disposed)
                return;

            var state = new HistoryViewState(result.FeedPage.Videos, result.StatusMessage, false,
                result.Status is AuthenticatedHistoryStatus.Success or AuthenticatedHistoryStatus.Empty,
                result.Status,
                false,
                result is { Status: AuthenticatedHistoryStatus.Success, FeedPage.ContinuationToken: not null });
            State = state;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (generation != _requestGeneration || _disposed)
                return;

            Logger.Warning(exception, "Failed to refresh watch history");
            const string message = "Could not load watch history.";
            State = new HistoryViewState([], message, false, false, AuthenticatedHistoryStatus.TemporaryBackendFailure);
        }
    }

    public async Task LoadMoreAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        ThrowIfDisposed();
        _lastRequestedCount = count;
        if (State is { IsLoading: true } or { IsLoadingMore: true } || !State.HasMore)
            return;
        _requestCancellation?.Dispose();
        _requestCancellation = new CancellationTokenSource();
        var token = _requestCancellation.Token;
        var generation = ++_requestGeneration;
        State = State with { IsLoadingMore = true, Summary = "Loading more watch history…" };
        try
        {
            var result = await historyService.LoadNextPageAsync(count, token).ConfigureAwait(false);
            if (token.IsCancellationRequested || generation != _requestGeneration || _disposed)
                return;

            var videos = State.Videos.Concat(result.FeedPage.Videos).DistinctBy(video => video.Id).ToArray();
            var isSuccess = result.Status is AuthenticatedHistoryStatus.Success or AuthenticatedHistoryStatus.Empty;
            var state = new HistoryViewState(videos, result.StatusMessage, false, isSuccess, result.Status, false,
                result is { Status: AuthenticatedHistoryStatus.Success, FeedPage.ContinuationToken: not null });
            State = state;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (generation != _requestGeneration || _disposed)
                return;

            Logger.Warning(exception, "Failed to load more watch history");
            const string message = "Could not load more watch history.";
            State = State with { Summary = message, IsLoadingMore = false };
        }
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