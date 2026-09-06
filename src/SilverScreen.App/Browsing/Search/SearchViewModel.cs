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
    /// <summary>
    /// Single generic status line for every pasted URL that is not a playable video or Shorts
    /// (channel, playlist, unknown or invalid). Shell shows the returned notice as a toast;
    /// the search page also surfaces it in its empty status.
    /// </summary>
    public const string UnsupportedUrlMessage =
        "That link isn't playable yet — paste a video or Shorts URL.";

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

    public Func<string, Task>? OpenChannelRequested { get; set; }

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
            OnPropertyChanged(nameof(BackLabel));
            StateChanged?.Invoke(this, value);
        }
    } = new([], "Search results will appear here.", false);

    /// <summary>
    /// Label for the shell back button while the search page is visible. Read by MainWindow;
    /// kept as a property (not a constant) so it can become query-aware without a shell change.
    /// </summary>
    public string BackLabel => "Exit Search";
    public string Summary => State.Summary;
    public bool IsLoading => State.IsLoading;
    public bool IsLoadingMore => State.IsLoadingMore;
    public bool HasMore => State.HasMore;

    /// <summary>
    /// Checks if the provided text represents a direct YouTube video or Shorts URL.
    /// </summary>
    public static bool IsDirectVideoUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parsed = YouTubeUrlParser.Parse(text.Trim());
        return parsed.Kind is YouTubeUrlKind.Video or YouTubeUrlKind.Shorts;
    }

    /// <summary>
    /// Checks if the provided text represents a YouTube channel URL or handle.
    /// </summary>
    public static bool IsChannelTarget(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        if (trimmed.StartsWith('@') && !trimmed.Contains(' '))
            return true;

        var parsed = YouTubeUrlParser.Parse(trimmed);
        return parsed.Kind == YouTubeUrlKind.Channel;
    }

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

    /// <summary>
    /// Handles a submitted search entry. Returns null when the submit was handled cleanly
    /// (video/Shorts playback started or a text search launched); otherwise returns a
    /// user-visible guidance string the shell should surface as a transient toast.
    /// Search-page state always reflects the outcome too, so nothing fails silently.
    /// </summary>
    public async Task<string?> SubmitAsync(string text, int count = VideoFeedConstants.DefaultPageSize)
    {
        Logger.Information("Search submitted: {Text}", text);
        var query = text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            ShowUnsupportedUrlNotice();
            return UnsupportedUrlMessage;
        }

        try
        {
            var parsedUrl = YouTubeUrlParser.Parse(query);
            if (parsedUrl.Kind == YouTubeUrlKind.Channel)
            {
                var channelTarget = parsedUrl.ChannelPath ?? query;
                if (OpenChannelRequested is not null)
                {
                    await OpenChannelRequested(channelTarget).ConfigureAwait(false);
                    return null;
                }
            }
            else if (query.StartsWith('@') && !query.Contains(' ') && OpenChannelRequested is not null)
            {
                await OpenChannelRequested(query).ConfigureAwait(false);
                return null;
            }

            switch (parsedUrl.Kind)
            {
                case YouTubeUrlKind.Video:
                case YouTubeUrlKind.Shorts:
                    return await PlayYouTubeUrlAsync(parsedUrl).ConfigureAwait(false);
                case YouTubeUrlKind.Channel:
                case YouTubeUrlKind.Playlist:
                case YouTubeUrlKind.UnknownYouTube:
                case YouTubeUrlKind.Invalid:
                    ShowUnsupportedUrlNotice();
                    return UnsupportedUrlMessage;
                case YouTubeUrlKind.NotYouTube:
                    await SearchPlainTextAsync(query, count).ConfigureAwait(false);
                    return null;
                default:
                    ShowUnsupportedUrlNotice();
                    return UnsupportedUrlMessage;
            }
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to submit search or play URL for query {Query}", query);
            return "Could not complete search. Check your network connection and try again.";
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

        var result = await _searchService.SearchAsync(
                new SearchRequest(query, count, token), ct)
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

    /// <summary>
    /// Surfaces the generic unsupported-link status on the search page itself (via the engine
    /// status the page already renders) and in view state, so a pasted non-video URL is never
    /// a silent no-op. Cancels any in-flight text search first so it cannot overwrite the notice.
    /// </summary>
    private void ShowUnsupportedUrlNotice()
    {
        ThrowIfDisposed();
        CurrentQuery = null;
        _engine.Reset(statusMessage: UnsupportedUrlMessage);
        State = new SearchViewState([], UnsupportedUrlMessage, false);
    }

    private async Task<string?> PlayYouTubeUrlAsync(YouTubeUrlParseResult parsedUrl)
    {
        if (parsedUrl.VideoId is null || parsedUrl.CanonicalWatchUrl is null)
        {
            ShowUnsupportedUrlNotice();
            return UnsupportedUrlMessage;
        }

        var video = new VideoSummary(parsedUrl.VideoId, $"YouTube video {parsedUrl.VideoId}", "YouTube", TimeSpan.Zero,
            string.Empty, false, parsedUrl.CanonicalWatchUrl);
        try
        {
            var result = await _playbackService.PlayAsync(new PlaybackRequest([video])).ConfigureAwait(false);
            if (!PlaybackResult.IsSuccessStatus(result))
            {
                Logger.Warning("Playback failed for pasted URL {VideoId}: {Result}", parsedUrl.VideoId, result);
                return result;
            }

            return null;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to start playback for pasted URL {VideoId}", parsedUrl.VideoId);
            return "Could not start playback. Check your network connection and try again.";
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