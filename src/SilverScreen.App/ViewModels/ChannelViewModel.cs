using System.ComponentModel;
using System.Runtime.CompilerServices;
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
    bool IsSuccess)
{
    public static ChannelViewState Empty { get; } = new(null, string.Empty, null, null, null, [],
        ChannelVideoSort.Newest, string.Empty, false, true);
}

public sealed class ChannelViewModel(IChannelService channelService, IStatusReporter shell) : IDisposable
{
    private bool _disposed;
    private CancellationTokenSource? _requestCancellation;
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

    public void Clear()
    {
        ThrowIfDisposed();
        ++_requestGeneration;
        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();
        _requestCancellation = null;
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
        var loadingState = State with
        {
            Url = channelUrl,
            Name = fallbackName,
            Sort = sort,
            Summary = $"Loading {fallbackName}…",
            IsLoading = true,
            IsSuccess = true,
            Videos = []
        };
        State = loadingState;
        shell.ReportStatus(loadingState.Summary);

        try
        {
            var page = await channelService.GetChannelAsync(channelUrl, fallbackName, sort, token).ConfigureAwait(false);
            if (token.IsCancellationRequested || generation != _requestGeneration || _disposed)
                return;

            var summary = page.StatusMessage ?? $"Showing {page.Videos.Count} video{(page.Videos.Count == 1 ? string.Empty : "s")} from {page.Name}.";
            State = new ChannelViewState(page.Url, page.Name, page.Description, page.AvatarUrl, page.SubscriberCount,
                page.Videos, page.Sort, summary, false, page.IsSuccess);
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
