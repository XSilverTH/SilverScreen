using System.ComponentModel;
using System.Runtime.CompilerServices;
using Serilog;
using SilverScreen.Browsing.Components;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Search;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;

namespace SilverScreen.Browsing.Search;

public sealed record SearchViewState(
    IReadOnlyList<VideoSummary> Videos,
    string Summary,
    bool IsLoading,
    bool IsLoadingMore = false,
    bool HasMore = false,
    bool IsSuccess = true);

public sealed class SearchViewModel : INotifyPropertyChanged, IVideoListSource
{
    private static readonly ILogger Logger = Log.ForContext<SearchViewModel>();
    private readonly PagedFeedEngine _engine;
    private readonly Lock _lock = new();
    private readonly IPlaybackService _playbackService;
    private readonly ISearchService _searchService;
    private readonly ISearchSuggestionService? _suggestionService;
    private bool _disposed;

    public SearchViewModel(
        ISearchService searchService,
        IPlaybackService playbackService,
        ISearchSuggestionService? suggestionService = null)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
        _suggestionService = suggestionService;

        _engine = new PagedFeedEngine(
            FetchSearchPageAsync,
            (_, _, state) => SearchVideoListSource.MapStatus(state),
            "Searching YouTube…",
            "Loading more results…",
            new VideoListStatus("No results found", "Search results will appear here.", "system-search-symbolic"),
            "Search",
            clearOnRefresh: true);

        _engine.EngineStateChanged += OnEngineStateChanged;
    }

    public SearchViewState State
    {
        get;
        private set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLoading));
            OnPropertyChanged(nameof(IsLoadingMore));
            OnPropertyChanged(nameof(HasMore));
            StateChanged?.Invoke(this, value);
        }
    } = new([], "Search results will appear here.", false);

    public string Summary => State.Summary;
    public bool IsLoading => State.IsLoading;
    public bool IsLoadingMore => State.IsLoadingMore;
    public bool HasMore => State.HasMore;

    public string? CurrentQuery
    {
        get
        {
            lock (_lock)
            {
                return field;
            }
        }
        private set
        {
            lock (_lock)
            {
                field = value;
            }
        }
    }

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
        return CurrentQuery is null ? Task.CompletedTask : _engine.RefreshAsync(count);
    }

    public Task LoadMoreAsync(int count = VideoFeedConstants.DefaultPageSize)
    {
        ThrowIfDisposed();
        return _engine.LoadMoreAsync(count);
    }

    public event EventHandler<SearchViewState>? StateChanged;

    public void Reset()
    {
        ThrowIfDisposed();
        CurrentQuery = null;
        _engine.Reset(
            new VideoListStatus("No results found", "Search results will appear here.", "system-search-symbolic"),
            "Search results will appear here.");
    }

    public async Task<IReadOnlyList<string>> FetchSuggestionsAsync(string text,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var query = text.Trim();
        if (string.IsNullOrWhiteSpace(query) || _suggestionService is null)
            return [];

        try
        {
            return await _suggestionService.GetSuggestionsAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to fetch search suggestions for query {Query}", query);
            return [];
        }
    }

    public async Task SubmitAsync(string text, int count = VideoFeedConstants.DefaultPageSize)
    {
        Logger.Information("Search submitted: {Text}", text);
        var query = text.Trim();
        if (string.IsNullOrWhiteSpace(query)) return;

        try
        {
            var parsedUrl = YouTubeUrlParser.Parse(query);
            switch (parsedUrl.Kind)
            {
                case YouTubeUrlKind.Video:
                    await PlayYouTubeUrlAsync(parsedUrl);
                    return;
                case YouTubeUrlKind.Shorts:
                case YouTubeUrlKind.Channel:
                case YouTubeUrlKind.Playlist:
                case YouTubeUrlKind.UnknownYouTube:
                case YouTubeUrlKind.Invalid:
                    return;
                case YouTubeUrlKind.NotYouTube:
                    await SearchPlainTextAsync(query, count);
                    return;
                default:
                    return;
            }
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to submit search or play URL for query {Query}", query);
        }
    }

    private async Task SearchPlainTextAsync(string query, int count = VideoFeedConstants.DefaultPageSize)
    {
        ThrowIfDisposed();
        CurrentQuery = query;
        _engine.SetLoadingMessage($"Searching YouTube for “{query}”…");
        await _engine.RefreshAsync(count).ConfigureAwait(false);
    }

    private async Task<FeedPageResult> FetchSearchPageAsync(string? token, int count, CancellationToken ct)
    {
        var query = CurrentQuery;
        if (query is null)
            return FeedPageResult.Empty;

        var startIndex = token is not null && int.TryParse(token, out var idx) ? idx : 1;
        var result = await _searchService.SearchAsync(new SearchRequest(query, startIndex, count), ct)
            .ConfigureAwait(false);

        return new FeedPageResult(
            result.Videos,
            result.IsSuccess ? result.ContinuationToken : null,
            result.IsSuccess,
            result.StatusMessage ?? (result.IsSuccess ? "Search complete." : "Search failed."));
    }

    private void OnEngineStateChanged(object? sender, FeedEngineState state)
    {
        var query = CurrentQuery;
        var summary = state.IsLoading
            ? query != null ? $"Searching YouTube for “{query}”… " : "Searching YouTube…"
            : state.IsLoadingMore
                ? "Loading more results…"
                : !state.IsSuccess || state.LastError != null
                    ? state.IsLoadingMore
                        ? "Search could not be completed."
                        : state.StatusMessage ?? "Search could not be completed."
                    : query is null
                        ? "Search results will appear here."
                        : state.StatusMessage ?? "Search complete.";

        State = new SearchViewState(
            state.Videos,
            summary,
            state.IsLoading,
            state.IsLoadingMore,
            state.HasMore,
            state is { IsSuccess: true, LastError: null });
    }

    private async Task PlayYouTubeUrlAsync(YouTubeUrlParseResult parsedUrl)
    {
        if (parsedUrl.VideoId is null || parsedUrl.CanonicalWatchUrl is null) return;

        var video = new VideoSummary(parsedUrl.VideoId, $"YouTube video {parsedUrl.VideoId}", "YouTube", TimeSpan.Zero,
            string.Empty, false, parsedUrl.CanonicalWatchUrl);
        await _playbackService.PlayAsync(new PlaybackRequest([video])).ConfigureAwait(false);
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