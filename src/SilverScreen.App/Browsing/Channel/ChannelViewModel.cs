using System.ComponentModel;
using System.Runtime.CompilerServices;
using Serilog;
using SilverScreen.Browsing.Components;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Browsing.Channel;

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

public sealed class ChannelViewModel : INotifyPropertyChanged, IVideoListSource
{
    private static readonly ILogger Logger = Log.ForContext<ChannelViewModel>();
    private readonly IChannelService _channelService;
    private readonly PagedFeedEngine _engine;
    private readonly Lock _lock = new();
    private string? _avatarUrl;
    private string? _description;
    private bool _disposed;
    private string _name = string.Empty;
    private ChannelVideoSort _sort = ChannelVideoSort.Newest;
    private long? _subscriberCount;
    private string? _url;

    public ChannelViewModel(IChannelService channelService)
    {
        _channelService = channelService ?? throw new ArgumentNullException(nameof(channelService));

        _engine = new PagedFeedEngine(
            FetchChannelPageAsync,
            (_, _, state) => ChannelVideoListSource.MapStatus(state),
            "Loading channel…",
            defaultTitle: "Channel",
            clearOnRefresh: true);

        _engine.EngineStateChanged += OnEngineStateChanged;
    }

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
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
        return State.Url is null
            ? Task.CompletedTask
            : LoadAsync(State.Url, State.Name, State.Sort, count);
    }

    public Task LoadMoreAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        ThrowIfDisposed();
        return _engine.LoadMoreAsync(count);
    }

    public event EventHandler<ChannelViewState>? StateChanged;

    public async Task OpenChannelAsync(string channelUrl, string fallbackName,
        int count = VideoFeedConstants.DefaultPageSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelUrl);
        Logger.Information("Opening channel {ChannelUrl} (FallbackName: {FallbackName})", channelUrl, fallbackName);
        await LoadAsync(channelUrl, fallbackName, ChannelVideoSort.Newest, count).ConfigureAwait(false);
    }

    public Task SetSortSelection(uint selected, int count = VideoFeedConstants.DefaultPageSize)
    {
        var sort = selected switch
        {
            0 => ChannelVideoSort.Newest,
            1 => ChannelVideoSort.Oldest,
            2 => ChannelVideoSort.Popular,
            _ => throw new ArgumentOutOfRangeException(nameof(selected), selected, null)
        };
        return SetSortAsync(sort, count);
    }

    private Task SetSortAsync(ChannelVideoSort sort, int count = VideoFeedConstants.DefaultPageSize)
    {
        return State.Url is null ? Task.CompletedTask : LoadAsync(State.Url, State.Name, sort, count);
    }

    public void Clear()
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            _url = null;
            _name = string.Empty;
            _description = null;
            _avatarUrl = null;
            _subscriberCount = null;
        }

        _engine.Reset();
        State = ChannelViewState.Empty;
    }

    private async Task LoadAsync(string channelUrl, string fallbackName, ChannelVideoSort sort,
        int count = VideoFeedConstants.DefaultPageSize)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            _url = channelUrl;
            _name = fallbackName;
            _sort = sort;
        }

        _engine.SetLoadingMessage($"Loading {fallbackName}…");
        await _engine.RefreshAsync(count).ConfigureAwait(false);
    }

    private async Task<FeedPageResult> FetchChannelPageAsync(string? token, int count, CancellationToken ct)
    {
        string? url;
        string name;
        ChannelVideoSort sort;
        lock (_lock)
        {
            url = _url;
            name = _name;
            sort = _sort;
        }

        if (url is null)
            return FeedPageResult.Empty;

        var startIndex = token is not null && int.TryParse(token, out var idx) ? idx : 1;
        var page = await _channelService.GetChannelAsync(url, name, sort, startIndex, count, ct).ConfigureAwait(false);

        if (!page.IsSuccess)
            return new FeedPageResult(
                page.Videos,
                page.IsSuccess ? page.NextStartIndex?.ToString() : null,
                page.IsSuccess,
                page.StatusMessage);
        lock (_lock)
        {
            _url = page.Url;
            _name = page.Name;
            _description = page.Description;
            _avatarUrl = page.AvatarUrl;
            _subscriberCount = page.SubscriberCount;
        }

        return new FeedPageResult(
            page.Videos,
            page.IsSuccess ? page.NextStartIndex?.ToString() : null,
            page.IsSuccess,
            page.StatusMessage);
    }

    private void OnEngineStateChanged(object? sender, FeedEngineState state)
    {
        string? url;
        string name;
        string? description;
        string? avatarUrl;
        long? subscriberCount;
        ChannelVideoSort sort;

        lock (_lock)
        {
            url = _url;
            name = _name;
            description = _description;
            avatarUrl = _avatarUrl;
            subscriberCount = _subscriberCount;
            sort = _sort;
        }

        var summary = state.IsLoading
            ? $"Loading {name}…"
            : state.IsLoadingMore
                ? "Loading more videos…"
                : !state.IsSuccess || state.LastError != null
                    ? state.IsLoadingMore ? "Could not load more channel videos." : "Could not load channel."
                    : state.StatusMessage ??
                      $"Showing {state.Videos.Count} video{(state.Videos.Count == 1 ? string.Empty : "s")} from {name}.";

        State = new ChannelViewState(
            url,
            name,
            description,
            avatarUrl,
            subscriberCount,
            state.Videos,
            sort,
            summary,
            state.IsLoading,
            state is { IsSuccess: true, LastError: null },
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