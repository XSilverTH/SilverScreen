using System.ComponentModel;
using System.Runtime.CompilerServices;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Search;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Queue;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Browsing.Search;

public sealed record SearchViewState(
    IReadOnlyList<VideoSummary> Videos,
    string Summary,
    bool IsLoading,
    bool IsLoadingMore = false,
    bool HasMore = false,
    bool IsSuccess = true);

public sealed class SearchViewModel(
    ISearchService searchService,
    IPlaybackService playbackService,
    ISearchSuggestionService? suggestionService = null)
    : INotifyPropertyChanged, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<SearchViewModel>();
    private string? _continuationToken;
    private bool _disposed;
    private CancellationTokenSource? _requestCancellation;
    private long _requestGeneration;

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
    public string? CurrentQuery { get; private set; }


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

    public void Reset()
    {
        ThrowIfDisposed();
        ++_requestGeneration;
        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();
        _requestCancellation = null;
        _continuationToken = null;
        CurrentQuery = null;
        State = new SearchViewState([], "Search results will appear here.", false);
    }

    public async Task<IReadOnlyList<string>> FetchSuggestionsAsync(string text,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var query = text.Trim();
        if (string.IsNullOrWhiteSpace(query) || suggestionService is null)
            return [];

        try
        {
            return await suggestionService.GetSuggestionsAsync(query, cancellationToken).ConfigureAwait(false);
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

    public event EventHandler<SearchViewState>? StateChanged;

    public async Task SubmitAsync(string text)
    {
        Logger.Information("Search submitted: {Text}", text);
        var query = text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

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
                    await SearchPlainTextAsync(query);
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

    public Task RefreshAsync()
    {
        return CurrentQuery is null ? Task.CompletedTask : SearchPlainTextAsync(CurrentQuery);
    }

    public async Task LoadMoreAsync()
    {
        ThrowIfDisposed();
        if (State is { IsLoading: true } or { IsLoadingMore: true } || CurrentQuery is null ||
            !int.TryParse(_continuationToken, out var startIndex) || startIndex < 1)
            return;

        _requestCancellation?.Dispose();
        _requestCancellation = new CancellationTokenSource();
        var token = _requestCancellation.Token;
        var generation = ++_requestGeneration;
        var loadingState = State with { IsLoadingMore = true, Summary = "Loading more results…" };
        State = loadingState;
        try
        {
            var result = await searchService.SearchAsync(new SearchRequest(CurrentQuery, startIndex), token)
                .ConfigureAwait(false);
            if (token.IsCancellationRequested || generation != _requestGeneration || _disposed)
                return;

            var videos = State.Videos.Concat(NormalizeVideos(result.Videos)).DistinctBy(video => video.Id).ToArray();
            _continuationToken = result.IsSuccess ? result.ContinuationToken : _continuationToken;
            var summary = result.StatusMessage ?? (result.IsSuccess ? "Search complete." : "Search failed.");
            State = new SearchViewState(videos, summary, false, false,
                result.IsSuccess && _continuationToken is not null, result.IsSuccess);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (generation != _requestGeneration || _disposed)
                return;

            Logger.Warning(exception, "Failed to load more search results for query {Query}", CurrentQuery);
            const string message = "Search could not be completed.";
            State = State with { Summary = message, IsLoadingMore = false, IsSuccess = false };
        }
    }
    private async Task SearchPlainTextAsync(string query)
    {
        ThrowIfDisposed();
        if (_requestCancellation is not null)
            await _requestCancellation.CancelAsync();

        _requestCancellation?.Dispose();
        _requestCancellation = new CancellationTokenSource();
        var token = _requestCancellation.Token;
        var generation = ++_requestGeneration;

        CurrentQuery = query;
        _continuationToken = null;
        var searching = $"Searching YouTube for “{query}”…";
        State = new SearchViewState([], searching, true);

        try
        {
            var result = await searchService.SearchAsync(new SearchRequest(query), token).ConfigureAwait(false);
            if (token.IsCancellationRequested || generation != _requestGeneration || _disposed)
                return;

            _continuationToken = result.IsSuccess ? result.ContinuationToken : null;
            var summary = result.StatusMessage ?? (result.IsSuccess ? "Search complete." : "Search failed.");
            State = new SearchViewState(NormalizeVideos(result.Videos), summary, false, false,
                result.IsSuccess && _continuationToken is not null, result.IsSuccess);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (generation != _requestGeneration || _disposed)
                return;

            Logger.Warning(exception, "Failed to execute search for query {Query}", query);
            const string message = "Search could not be completed.";
            _continuationToken = null;
            State = new SearchViewState([], message, false, false, false, false);
        }
    }
    private static VideoSummary[] NormalizeVideos(IReadOnlyList<VideoSummary> videos)
    {
        return [.. videos.Where(video => !video.IsShort).DistinctBy(video => video.Id)];
    }

    private async Task PlayYouTubeUrlAsync(YouTubeUrlParseResult parsedUrl)
    {
        if (parsedUrl.VideoId is null || parsedUrl.CanonicalWatchUrl is null)
        {
            return;
        }

        var video = new VideoSummary(parsedUrl.VideoId, $"YouTube video {parsedUrl.VideoId}", "YouTube", TimeSpan.Zero,
            string.Empty, false, parsedUrl.CanonicalWatchUrl);
        await playbackService.PlayAsync(new PlaybackRequest([video])).ConfigureAwait(false);
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