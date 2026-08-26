using Serilog;
using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Browsing.Components;

public sealed record FeedPageResult(
    IReadOnlyList<VideoSummary> Videos,
    string? ContinuationToken = null,
    bool IsSuccess = true,
    string? StatusMessage = null,
    bool ClearExistingOnFailure = false)
{
    public static FeedPageResult Empty { get; } = new([]);
    public static FeedPageResult Failed(string message, bool clearExisting = false) =>
        new([], null, false, message, clearExisting);
}

public delegate Task<FeedPageResult> FeedPageFetcher(
    string? continuationToken,
    int count,
    CancellationToken cancellationToken);

public delegate VideoListStatus FeedStatusMapper(
    FeedPageResult? lastResult,
    Exception? error,
    FeedEngineState state);

public sealed record FeedEngineState(
    IReadOnlyList<VideoSummary> Videos,
    bool IsLoading,
    bool IsLoadingMore,
    bool HasMore,
    string? ContinuationToken,
    string? StatusMessage,
    bool IsSuccess,
    Exception? LastError);

public class PagedFeedEngine : IVideoListSource
{
    private static readonly ILogger Logger = Log.ForContext<PagedFeedEngine>();

    private readonly Lock _lock = new();
    private readonly List<VideoSummary> _videos = [];
    private FeedPageFetcher? _fetcher;
    private FeedStatusMapper? _statusMapper;
    private string? _loadingMessage;
    private string _paginationLoadingMessage = "Loading more videos…";
    private string _defaultTitle = "Videos";
    private string _defaultEmptyTitle = "No videos found";
    private string _defaultEmptyDescription = "No videos are available right now.";
    private string _defaultEmptyIcon = "applications-internet-symbolic";
    private string _defaultIcon = "video-x-generic-symbolic";
    private bool _clearOnRefresh = true;

    private CancellationTokenSource? _cts;
    private long _currentGeneration;
    private string? _continuationToken;
    private bool _isLoading;
    private bool _isLoadingMore;
    private bool _hasMore;
    private bool _isSuccess = true;
    private string? _statusMessage;
    private FeedPageResult? _lastResult;
    private Exception? _lastError;
    private VideoListStatus? _explicitStatus;
    private bool _disposed;

    public PagedFeedEngine(
        FeedPageFetcher? fetcher = null,
        FeedStatusMapper? statusMapper = null,
        string? loadingMessage = null,
        string paginationLoadingMessage = "Loading more videos…",
        VideoListStatus? initialStatus = null,
        string defaultTitle = "Videos",
        string defaultEmptyTitle = "No videos found",
        string defaultEmptyDescription = "No videos are available right now.",
        string defaultEmptyIcon = "applications-internet-symbolic",
        string defaultIcon = "video-x-generic-symbolic",
        bool clearOnRefresh = true)
    {
        _fetcher = fetcher;
        _statusMapper = statusMapper;
        _loadingMessage = loadingMessage;
        _paginationLoadingMessage = paginationLoadingMessage;
        _explicitStatus = initialStatus;
        _defaultTitle = defaultTitle;
        _defaultEmptyTitle = defaultEmptyTitle;
        _defaultEmptyDescription = defaultEmptyDescription;
        _defaultEmptyIcon = defaultEmptyIcon;
        _defaultIcon = defaultIcon;
        _clearOnRefresh = clearOnRefresh;

        UpdateStateUnsafe();
    }

    public VideoListPresentationState State { get; private set; } = default!;
    public FeedEngineState EngineState { get; private set; } = default!;

    public IReadOnlyList<VideoSummary> Videos
    {
        get
        {
            lock (_lock)
                return [.. _videos];
        }
    }

    public bool IsLoading
    {
        get
        {
            lock (_lock)
                return _isLoading;
        }
    }

    public bool IsLoadingMore
    {
        get
        {
            lock (_lock)
                return _isLoadingMore;
        }
    }

    public bool HasMore
    {
        get
        {
            lock (_lock)
                return _hasMore;
        }
    }

    public string? ContinuationToken
    {
        get
        {
            lock (_lock)
                return _continuationToken;
        }
    }

    public bool IsSuccess
    {
        get
        {
            lock (_lock)
                return _isSuccess;
        }
    }

    public string? StatusMessage
    {
        get
        {
            lock (_lock)
                return _statusMessage;
        }
    }

    public event EventHandler<VideoListPresentationState>? StateChanged;
    public event EventHandler<FeedEngineState>? EngineStateChanged;

    public static PagedFeedEngine Create<TPage>(
        Func<int, CancellationToken, Task<TPage>> fetchFirstPage,
        Func<string?, int, CancellationToken, Task<TPage>> fetchNextPage,
        Func<TPage, FeedPageResult> extractResult,
        FeedStatusMapper? statusMapper = null,
        string? loadingMessage = null,
        string paginationLoadingMessage = "Loading more videos…",
        VideoListStatus? initialStatus = null,
        string defaultTitle = "Videos",
        string defaultEmptyTitle = "No videos found",
        string defaultEmptyDescription = "No videos are available right now.",
        string defaultEmptyIcon = "applications-internet-symbolic",
        string defaultIcon = "video-x-generic-symbolic",
        bool clearOnRefresh = true)
    {
        return new PagedFeedEngine(
            fetcher: async (token, count, ct) =>
            {
                var page = token is null
                    ? await fetchFirstPage(count, ct).ConfigureAwait(false)
                    : await fetchNextPage(token, count, ct).ConfigureAwait(false);
                return extractResult(page);
            },
            statusMapper: statusMapper,
            loadingMessage: loadingMessage,
            paginationLoadingMessage: paginationLoadingMessage,
            initialStatus: initialStatus,
            defaultTitle: defaultTitle,
            defaultEmptyTitle: defaultEmptyTitle,
            defaultEmptyDescription: defaultEmptyDescription,
            defaultEmptyIcon: defaultEmptyIcon,
            defaultIcon: defaultIcon,
            clearOnRefresh: clearOnRefresh);
    }

    public void Configure(
        FeedPageFetcher fetcher,
        FeedStatusMapper? statusMapper = null,
        string? loadingMessage = null,
        VideoListStatus? initialStatus = null)
    {
        lock (_lock)
        {
            _fetcher = fetcher;
            if (statusMapper != null) _statusMapper = statusMapper;
            if (loadingMessage != null) _loadingMessage = loadingMessage;
            if (initialStatus != null) _explicitStatus = initialStatus;
            UpdateStateUnsafe();
        }
    }

    public void SetLoadingMessage(string? loadingMessage)
    {
        lock (_lock)
        {
            _loadingMessage = loadingMessage;
            UpdateStateUnsafe();
        }
    }

    public void SetStatus(VideoListStatus status)
    {
        lock (_lock)
        {
            _explicitStatus = status;
            UpdateStateUnsafe();
        }
        PublishState();
    }

    public void SetVideos(
        IReadOnlyList<VideoSummary> videos,
        string? continuationToken = null,
        bool? hasMore = null,
        VideoListStatus? status = null,
        string? statusMessage = null,
        bool isSuccess = true)
    {
        lock (_lock)
        {
            _videos.Clear();
            _videos.AddRange(videos.Where(v => !v.IsShort).DistinctBy(v => v.Id));
            _continuationToken = continuationToken;
            _hasMore = hasMore ?? !string.IsNullOrEmpty(continuationToken);
            _isSuccess = isSuccess;
            _statusMessage = statusMessage;
            _lastError = null;
            if (status != null) _explicitStatus = status;
            UpdateStateUnsafe();
        }
        PublishState();
    }

    public void Reset(VideoListStatus? status = null, string? statusMessage = null)
    {
        lock (_lock)
        {
            CancelPendingRequestsUnsafe();
            _videos.Clear();
            _continuationToken = null;
            _isLoading = false;
            _isLoadingMore = false;
            _hasMore = false;
            _isSuccess = true;
            _statusMessage = statusMessage;
            _lastResult = null;
            _lastError = null;
            _explicitStatus = status;
            UpdateStateUnsafe();
        }
        PublishState();
    }

    public async Task RefreshAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        if (_disposed) return;
        await ExecuteFetchAsync(isRefresh: true, count).ConfigureAwait(false);
    }

    public async Task LoadMoreAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        if (_disposed) return;
        lock (_lock)
        {
            if (_isLoading || _isLoadingMore || !_hasMore || _fetcher is null)
                return;
        }
        await ExecuteFetchAsync(isRefresh: false, count).ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            CancelPendingRequestsUnsafe();
        }
    }

    private void CancelPendingRequestsUnsafe()
    {
        ++_currentGeneration;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task ExecuteFetchAsync(bool isRefresh, int count)
    {
        CancellationToken token;
        long generation;
        string? tokenForFetch;
        FeedPageFetcher? fetcher;

        lock (_lock)
        {
            if (_disposed) return;

            CancelPendingRequestsUnsafe();
            generation = _currentGeneration;

            _cts = new CancellationTokenSource();
            token = _cts.Token;

            _isLoading = isRefresh;
            _isLoadingMore = !isRefresh;

            if (isRefresh)
            {
                _continuationToken = null;
                if (_clearOnRefresh)
                    _videos.Clear();
            }

            tokenForFetch = isRefresh ? null : _continuationToken;
            fetcher = _fetcher;
            _explicitStatus = null;

            UpdateStateUnsafe();
        }

        PublishState();

        if (fetcher is null)
        {
            lock (_lock)
            {
                if (generation != _currentGeneration || _disposed) return;
                _isLoading = false;
                _isLoadingMore = false;
                UpdateStateUnsafe();
            }
            PublishState();
            return;
        }

        try
        {
            var result = await fetcher(tokenForFetch, count, token).ConfigureAwait(false);

            if (token.IsCancellationRequested)
                return;

            lock (_lock)
            {
                if (generation != _currentGeneration || _disposed)
                    return;

                _lastResult = result;
                _lastError = null;
                _isSuccess = result.IsSuccess;
                _statusMessage = result.StatusMessage;

                if (result.IsSuccess)
                {
                    var newVideos = (result.Videos ?? [])
                        .Where(v => !v.IsShort)
                        .ToList();

                    if (isRefresh)
                    {
                        _videos.Clear();
                        _videos.AddRange(newVideos.DistinctBy(v => v.Id));
                    }
                    else
                    {
                        foreach (var video in newVideos)
                        {
                            if (_videos.All(v => v.Id != video.Id))
                                _videos.Add(video);
                        }
                    }

                    _continuationToken = result.ContinuationToken;
                    _hasMore = !string.IsNullOrEmpty(_continuationToken);
                }
                else
                {
                    if (result.ClearExistingOnFailure)
                    {
                        _videos.Clear();
                        _continuationToken = null;
                    }
                    _hasMore = false;
                }

                _isLoading = false;
                _isLoadingMore = false;

                UpdateStateUnsafe();
            }

            PublishState();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Cancelled or superseded — ignore
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                if (generation != _currentGeneration || _disposed)
                    return;

                Logger.Warning(ex, "Feed page fetch failed");
                _lastError = ex;
                _isSuccess = false;
                _hasMore = false;
                _isLoading = false;
                _isLoadingMore = false;

                UpdateStateUnsafe();
            }

            PublishState();
        }
    }

    private void UpdateStateUnsafe()
    {
        var videosSnapshot = _videos.ToArray();
        var engineState = new FeedEngineState(
            videosSnapshot,
            _isLoading,
            _isLoadingMore,
            _hasMore,
            _continuationToken,
            _statusMessage,
            _isSuccess,
            _lastError);

        var status = _explicitStatus
                     ?? _statusMapper?.Invoke(_lastResult, _lastError, engineState)
                     ?? ComputeDefaultStatus(_lastResult, _lastError, videosSnapshot, _isSuccess, _statusMessage);

        var presentationState = new VideoListPresentationState(
            videosSnapshot,
            _isLoading,
            _isLoadingMore,
            status,
            _isLoading ? _loadingMessage : null,
            _paginationLoadingMessage);

        EngineState = engineState;
        State = presentationState;
    }

    private VideoListStatus ComputeDefaultStatus(
        FeedPageResult? result,
        Exception? error,
        VideoSummary[] videos,
        bool isSuccess,
        string? statusMessage)
    {
        if (error is not null || !isSuccess)
        {
            var description = !string.IsNullOrWhiteSpace(statusMessage)
                ? statusMessage
                : "Failed to load videos. Check your network connection and try again.";

            return new VideoListStatus(
                _defaultTitle,
                description,
                "network-error-symbolic",
                ShowRetry: true);
        }

        if (videos.Length == 0)
        {
            var description = !string.IsNullOrWhiteSpace(statusMessage)
                ? statusMessage
                : _defaultEmptyDescription;

            return new VideoListStatus(
                _defaultEmptyTitle,
                description,
                _defaultEmptyIcon);
        }

        return new VideoListStatus(
            _defaultTitle,
            string.Empty,
            _defaultIcon);
    }

    private void PublishState()
    {
        VideoListPresentationState presentation;
        FeedEngineState engine;

        lock (_lock)
        {
            presentation = State;
            engine = EngineState;
        }

        StateChanged?.Invoke(this, presentation);
        EngineStateChanged?.Invoke(this, engine);
    }
}
