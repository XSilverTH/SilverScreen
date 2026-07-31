using System.ComponentModel;
using System.Runtime.CompilerServices;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.ViewModels;

public sealed record ChannelViewState(
    string? Url,
    string Name,
    string? Description,
    string? AvatarUrl,
    long? SubscriberCount,
    IReadOnlyList<VideoSummary> Videos,
    ChannelVideoSort Sort,
    string Summary,
    bool IsLoading,
    bool IsSuccess,
    bool IsLoadingMore = false,
    bool HasMore = false)
{
    public static ChannelViewState Empty { get; } = new(null, string.Empty, null, null, null, [],
        ChannelVideoSort.Newest, string.Empty, false, true);
}

public sealed class ChannelViewModel(IChannelService channelService, IStatusReporter shell) : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<ChannelViewModel>();
    private bool _disposed;
    private CancellationTokenSource? _requestCancellation;
    private int? _nextStartIndex;
    private long _requestGeneration;

    public ChannelViewState State
    {
        get;
        private set
        {
            field = value;
            OnPropertyChanged();
            StateChanged?.Invoke(this, value);
        }
    } = ChannelViewState.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<ChannelViewState>? StateChanged;

    public async Task OpenChannelAsync(string channelUrl, string fallbackName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelUrl);
        Logger.Information("Opening channel {ChannelUrl} (FallbackName: {FallbackName})", channelUrl, fallbackName);
        await LoadAsync(channelUrl, fallbackName, ChannelVideoSort.Newest).ConfigureAwait(false);
    }

    public Task SetSortSelection(uint selected)
    {
        var sort = selected switch
        {
            0 => ChannelVideoSort.Newest,
            1 => ChannelVideoSort.Oldest,
            2 => ChannelVideoSort.Popular,
            _ => throw new ArgumentOutOfRangeException(nameof(selected), selected, null)
        };
        return SetSortAsync(sort);
    }

    public Task SetSortAsync(ChannelVideoSort sort)
    {
        if (State.Url is null)
            return Task.CompletedTask;

        return LoadAsync(State.Url, State.Name, sort);
    }

    public Task RefreshAsync()
    {
        return State.Url is null
            ? Task.CompletedTask
            : LoadAsync(State.Url, State.Name, State.Sort);
    }

    public async Task LoadMoreAsync()
    {
        ThrowIfDisposed();
        if (State is { IsLoading: true } or { IsLoadingMore: true } || State.Url is null ||
            _nextStartIndex is not { } startIndex)
            return;

        _requestCancellation?.Dispose();
        _requestCancellation = new CancellationTokenSource();
        var token = _requestCancellation.Token;
        var generation = ++_requestGeneration;
        var loadingState = State with { IsLoadingMore = true, Summary = "Loading more videos…" };
        State = loadingState;
        shell.ReportStatus(loadingState.Summary);

        try
        {
            var page = await channelService.GetChannelAsync(State.Url, State.Name, State.Sort, startIndex, token)
                .ConfigureAwait(false);
            if (token.IsCancellationRequested || generation != _requestGeneration || _disposed)
                return;

            _nextStartIndex = page.IsSuccess ? page.NextStartIndex : _nextStartIndex;
            var videos = State.Videos.Concat(page.Videos).DistinctBy(video => video.Id).ToArray();
            var summary = page.StatusMessage ??
                          $"Showing {videos.Length} video{(videos.Length == 1 ? string.Empty : "s")} from {page.Name}.";
            State = new ChannelViewState(page.Url, page.Name, page.Description, page.AvatarUrl, page.SubscriberCount,
                videos, page.Sort, summary, false, page.IsSuccess, false, page.IsSuccess && _nextStartIndex is not null);
            shell.ReportStatus(summary);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (generation != _requestGeneration || _disposed)
                return;

            const string message = "Could not load more channel videos.";
            State = State with { Summary = message, IsLoadingMore = false };
            shell.ReportStatus(message);
        }
    }

    public void Clear()
    {
        ThrowIfDisposed();
        ++_requestGeneration;
        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();
        _requestCancellation = null;
        _nextStartIndex = null;
        State = ChannelViewState.Empty;
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
        _nextStartIndex = null;
    }

    private async Task LoadAsync(string channelUrl, string fallbackName, ChannelVideoSort sort)
    {
        ThrowIfDisposed();
        if (_requestCancellation is not null)
            await _requestCancellation.CancelAsync();

        _requestCancellation?.Dispose();
        _requestCancellation = new CancellationTokenSource();
        var token = _requestCancellation.Token;
        var generation = ++_requestGeneration;
        _nextStartIndex = null;
        var loadingState = State with
        {
            Url = channelUrl,
            Name = fallbackName,
            Sort = sort,
            Summary = $"Loading {fallbackName}…",
            IsLoading = true,
            IsLoadingMore = false,
            HasMore = false,
            IsSuccess = true,
            Videos = []
        };
        State = loadingState;
        shell.ReportStatus(loadingState.Summary);

        try
        {
            var page = await channelService.GetChannelAsync(channelUrl, fallbackName, sort, 1, token).ConfigureAwait(false);
            if (token.IsCancellationRequested || generation != _requestGeneration || _disposed)
                return;

            _nextStartIndex = page.IsSuccess ? page.NextStartIndex : null;
            var summary = page.StatusMessage ?? $"Showing {page.Videos.Count} video{(page.Videos.Count == 1 ? string.Empty : "s")} from {page.Name}.";
            State = new ChannelViewState(page.Url, page.Name, page.Description, page.AvatarUrl, page.SubscriberCount,
                page.Videos, page.Sort, summary, false, page.IsSuccess, false,
                page.IsSuccess && _nextStartIndex is not null);
            shell.ReportStatus(summary);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (generation != _requestGeneration || _disposed)
                return;

            const string message = "Could not load channel.";
            _nextStartIndex = null;
            State = State with { Summary = message, IsLoading = false, IsSuccess = false, Videos = [] };
            shell.ReportStatus(message);
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
