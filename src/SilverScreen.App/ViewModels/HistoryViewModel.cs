using System.ComponentModel;
using System.Runtime.CompilerServices;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.ViewModels;

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

public sealed class HistoryViewModel(IAuthenticatedHistoryService historyService, IStatusReporter shell) : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<HistoryViewModel>();
    private bool _disposed;
    private CancellationTokenSource? _requestCancellation;
    private long _requestGeneration;

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
    public event EventHandler<HistoryViewState>? StateChanged;

    public Task LoadAsync()
    {
        return State.Videos.Count == 0 && !State.IsLoading ? RefreshAsync() : Task.CompletedTask;
    }

    public async Task RefreshAsync()
    {
        Logger.Information("HistoryViewModel refreshing watch history");
        ThrowIfDisposed();

        _requestCancellation?.Dispose();
        _requestCancellation = new CancellationTokenSource();
        var token = _requestCancellation.Token;
        var generation = ++_requestGeneration;
        State = new HistoryViewState([], "Loading watch history…", true, true, AuthenticatedHistoryStatus.Success);
        shell.ReportStatus(State.Summary);

        try
        {
            var result = await historyService.LoadFirstPageAsync(token).ConfigureAwait(false);
            if (token.IsCancellationRequested || generation != _requestGeneration || _disposed)
                return;

            var state = new HistoryViewState(result.FeedPage.Videos, result.StatusMessage, false,
                result.Status is AuthenticatedHistoryStatus.Success or AuthenticatedHistoryStatus.Empty,
                result.Status,
                false,
                result.Status == AuthenticatedHistoryStatus.Success && result.FeedPage.ContinuationToken is not null);
            State = state;
            shell.ReportStatus(state.Summary);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (generation != _requestGeneration || _disposed)
                return;

            const string message = "Could not load watch history.";
            State = new HistoryViewState([], message, false, false, AuthenticatedHistoryStatus.TemporaryBackendFailure);
            shell.ReportStatus(message);
        }
    }

    public async Task LoadMoreAsync()
    {
        ThrowIfDisposed();
        if (State is { IsLoading: true } or { IsLoadingMore: true } || !State.HasMore)
            return;

        _requestCancellation?.Dispose();
        _requestCancellation = new CancellationTokenSource();
        var token = _requestCancellation.Token;
        var generation = ++_requestGeneration;
        State = State with { IsLoadingMore = true, Summary = "Loading more watch history…" };
        shell.ReportStatus(State.Summary);

        try
        {
            var result = await historyService.LoadNextPageAsync(token).ConfigureAwait(false);
            if (token.IsCancellationRequested || generation != _requestGeneration || _disposed)
                return;

            var videos = State.Videos.Concat(result.FeedPage.Videos).DistinctBy(video => video.Id).ToArray();
            var isSuccess = result.Status is AuthenticatedHistoryStatus.Success or AuthenticatedHistoryStatus.Empty;
            var state = new HistoryViewState(videos, result.StatusMessage, false, isSuccess, result.Status, false,
                result.Status == AuthenticatedHistoryStatus.Success && result.FeedPage.ContinuationToken is not null);
            State = state;
            shell.ReportStatus(state.Summary);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (generation != _requestGeneration || _disposed)
                return;

            const string message = "Could not load more watch history.";
            State = State with { Summary = message, IsLoadingMore = false };
            shell.ReportStatus(message);
        }
    }

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
