using System.ComponentModel;
using System.Runtime.CompilerServices;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.ViewModels;

public sealed record SearchViewState(
    IReadOnlyList<VideoSummary> Videos,
    string Summary,
    bool IsLoading,
    bool IsLoadingMore = false,
    bool HasMore = false);

public sealed class SearchViewModel(
    ISearchService searchService,
    IPlaybackService playbackService,
    IStatusReporter shell)
    : INotifyPropertyChanged, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<SearchViewModel>();
    private bool _disposed;
    private CancellationTokenSource? _requestCancellation;
    private long _requestGeneration;
    private string? _continuationToken;
    private string? _query;

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
        _query = null;
        State = new SearchViewState([], "Search results will appear here.", false);
    }

    public event EventHandler<SearchViewState>? StateChanged;

    public async Task SubmitAsync(string text)
    {
        Logger.Information("Search submitted: {Text}", text);
        var query = text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            shell.ReportStatus("Empty search ignored.");
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
                    shell.ReportStatus("Shorts are not supported in SilverScreen.");
                    return;
                case YouTubeUrlKind.Channel:
                    shell.ReportStatus("Channel pages are not implemented yet.");
                    return;
                case YouTubeUrlKind.Playlist:
                    shell.ReportStatus("Playlists are not implemented yet.");
                    return;
                case YouTubeUrlKind.UnknownYouTube:
                    shell.ReportStatus("Unsupported YouTube URL.");
                    return;
                case YouTubeUrlKind.Invalid:
                    shell.ReportStatus("Invalid YouTube URL.");
                    return;
                case YouTubeUrlKind.NotYouTube:
                    await SearchPlainTextAsync(query);
                    return;
                default:
                    shell.ReportStatus("Unsupported YouTube URL.");
                    return;
            }
        }
        catch (Exception)
        {
            shell.ReportStatus("The requested action could not be completed.");
        }
    }

    public Task RefreshAsync()
    {
        return _query is null ? Task.CompletedTask : SearchPlainTextAsync(_query);
    }

    public async Task LoadMoreAsync()
    {
        ThrowIfDisposed();
        if (State is { IsLoading: true } or { IsLoadingMore: true } || _query is null ||
            !int.TryParse(_continuationToken, out var startIndex) || startIndex < 1)
            return;

        _requestCancellation?.Dispose();
        _requestCancellation = new CancellationTokenSource();
        var token = _requestCancellation.Token;
        var generation = ++_requestGeneration;
        var loadingState = State with { IsLoadingMore = true, Summary = "Loading more results…" };
        State = loadingState;
        shell.ReportStatus(loadingState.Summary);

        try
        {
            var result = await searchService.SearchAsync(new SearchRequest(_query, startIndex), token).ConfigureAwait(false);
            if (token.IsCancellationRequested || generation != _requestGeneration || _disposed)
                return;

            var videos = State.Videos.Concat(NormalizeVideos(result.Videos)).DistinctBy(video => video.Id).ToArray();
            _continuationToken = result.IsSuccess ? result.ContinuationToken : _continuationToken;
            var summary = result.StatusMessage ?? (result.IsSuccess ? "Search complete." : "Search failed.");
            State = new SearchViewState(videos, summary, false, false, result.IsSuccess && _continuationToken is not null);
            shell.ReportStatus(summary);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (generation != _requestGeneration || _disposed)
                return;

            const string message = "Search could not be completed.";
            State = State with { Summary = message, IsLoadingMore = false };
            shell.ReportStatus(message);
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

        _query = query;
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
                result.IsSuccess && _continuationToken is not null);
            shell.ReportStatus(summary);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (generation != _requestGeneration || _disposed)
                return;

            const string message = "Search could not be completed.";
            _continuationToken = null;
            State = new SearchViewState([], message, false);
            shell.ReportStatus(message);
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
            shell.ReportStatus("Invalid YouTube URL.");
            return;
        }

        var video = new VideoSummary(parsedUrl.VideoId, $"YouTube video {parsedUrl.VideoId}", "YouTube", TimeSpan.Zero,
            string.Empty, false, parsedUrl.CanonicalWatchUrl);
        shell.ReportStatus(await playbackService.PlayAsync(new PlaybackRequest([video])).ConfigureAwait(false));
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