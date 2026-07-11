using System.ComponentModel;
using System.Runtime.CompilerServices;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Features.Search;

namespace SilverScreen.ViewModels;

public sealed record SearchViewState(
    IReadOnlyList<VideoSummary> Videos,
    string Summary,
    bool IsLoading);

public sealed class SearchViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ISearchService _searchService;
    private readonly IPlaybackService _playbackService;
    private readonly ShellViewModel _shell;
    private CancellationTokenSource? _requestCancellation;
    private long _requestGeneration;
    private SearchViewState _state = new([], "Search results will appear here.", false);
    private bool _disposed;

    public SearchViewModel(ISearchService searchService, IPlaybackService playbackService, ShellViewModel shell)
    {
        _searchService = searchService;
        _playbackService = playbackService;
        _shell = shell;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<SearchViewState>? StateChanged;

    public SearchViewState State
    {
        get => _state;
        private set
        {
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(IsLoading));
            StateChanged?.Invoke(this, value);
        }
    }

    public string Summary => State.Summary;
    public bool IsLoading => State.IsLoading;

    public async Task SubmitAsync(string text)
    {
        var query = text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            _shell.Status = "Empty search ignored.";
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
                    _shell.Status = "Shorts are not supported in SilverScreen.";
                    return;
                case YouTubeUrlKind.Channel:
                    _shell.Status = "Channel pages are not implemented yet.";
                    return;
                case YouTubeUrlKind.Playlist:
                    _shell.Status = "Playlists are not implemented yet.";
                    return;
                case YouTubeUrlKind.UnknownYouTube:
                    _shell.Status = "Unsupported YouTube URL.";
                    return;
                case YouTubeUrlKind.Invalid:
                    _shell.Status = "Invalid YouTube URL.";
                    return;
                case YouTubeUrlKind.NotYouTube:
                    await SearchPlainTextAsync(query);
                    return;
                default:
                    _shell.Status = "Unsupported YouTube URL.";
                    return;
            }
        }
        catch (Exception)
        {
            _shell.Status = "The requested action could not be completed.";
        }
    }

    private async Task SearchPlainTextAsync(string query)
    {
        ThrowIfDisposed();
        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();
        _requestCancellation = new CancellationTokenSource();
        var token = _requestCancellation.Token;
        var generation = ++_requestGeneration;

        _shell.SelectedPage = "search";
        var searching = $"Searching YouTube for “{query}”…";
        State = new SearchViewState([], searching, true);
        _shell.Status = searching;

        try
        {
            var result = await _searchService.SearchAsync(new SearchRequest(query), token).ConfigureAwait(false);
            if (token.IsCancellationRequested || generation != _requestGeneration || _disposed)
            {
                return;
            }

            var summary = result.StatusMessage ?? (result.IsSuccess ? "Search complete." : "Search failed.");
            State = new SearchViewState(result.Videos, summary, false);
            _shell.Status = summary;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (generation != _requestGeneration || _disposed)
            {
                return;
            }

            const string message = "Search could not be completed.";
            State = new SearchViewState([], message, false);
            _shell.Status = message;
        }
    }

    private async Task PlayYouTubeUrlAsync(YouTubeUrlParseResult parsedUrl)
    {
        if (parsedUrl.VideoId is null || parsedUrl.CanonicalWatchUrl is null)
        {
            _shell.Status = "Invalid YouTube URL.";
            return;
        }

        var video = new VideoSummary(parsedUrl.VideoId, $"YouTube video {parsedUrl.VideoId}", "YouTube", TimeSpan.Zero,
            string.Empty, false, parsedUrl.CanonicalWatchUrl);
        _shell.Status = await _playbackService.PlayAsync(new PlaybackRequest(video)).ConfigureAwait(false);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ++_requestGeneration;
        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();
        _requestCancellation = null;
    }
}
