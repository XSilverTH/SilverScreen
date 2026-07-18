using System.ComponentModel;
using System.Runtime.CompilerServices;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Search;

namespace SilverScreen.ViewModels;

public sealed record SearchViewState(
    IReadOnlyList<VideoSummary> Videos,
    string Summary,
    bool IsLoading);

public sealed class SearchViewModel(
    ISearchService searchService,
    IPlaybackService playbackService,
    ShellViewModel shell)
    : INotifyPropertyChanged, IDisposable
{
    private bool _disposed;
    private CancellationTokenSource? _requestCancellation;
    private long _requestGeneration;
    private SearchViewState _state = new([], "Search results will appear here.", false);

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
    public event EventHandler<SearchViewState>? StateChanged;

    public async Task SubmitAsync(string text)
    {
        var query = text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            shell.Status = "Empty search ignored.";
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
                    shell.Status = "Shorts are not supported in SilverScreen.";
                    return;
                case YouTubeUrlKind.Channel:
                    shell.Status = "Channel pages are not implemented yet.";
                    return;
                case YouTubeUrlKind.Playlist:
                    shell.Status = "Playlists are not implemented yet.";
                    return;
                case YouTubeUrlKind.UnknownYouTube:
                    shell.Status = "Unsupported YouTube URL.";
                    return;
                case YouTubeUrlKind.Invalid:
                    shell.Status = "Invalid YouTube URL.";
                    return;
                case YouTubeUrlKind.NotYouTube:
                    await SearchPlainTextAsync(query);
                    return;
                default:
                    shell.Status = "Unsupported YouTube URL.";
                    return;
            }
        }
        catch (Exception)
        {
            shell.Status = "The requested action could not be completed.";
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

        shell.SelectedPage = "search";
        var searching = $"Searching YouTube for “{query}”…";
        State = new SearchViewState([], searching, true);
        shell.Status = searching;

        try
        {
            var result = await searchService.SearchAsync(new SearchRequest(query), token).ConfigureAwait(false);
            if (token.IsCancellationRequested || generation != _requestGeneration || _disposed)
                return;

            var summary = result.StatusMessage ?? (result.IsSuccess ? "Search complete." : "Search failed.");
            State = new SearchViewState(result.Videos, summary, false);
            shell.Status = summary;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (generation != _requestGeneration || _disposed)
                return;

            const string message = "Search could not be completed.";
            State = new SearchViewState([], message, false);
            shell.Status = message;
        }
    }

    private async Task PlayYouTubeUrlAsync(YouTubeUrlParseResult parsedUrl)
    {
        if (parsedUrl.VideoId is null || parsedUrl.CanonicalWatchUrl is null)
        {
            shell.Status = "Invalid YouTube URL.";
            return;
        }

        var video = new VideoSummary(parsedUrl.VideoId, $"YouTube video {parsedUrl.VideoId}", "YouTube", TimeSpan.Zero,
            string.Empty, false, parsedUrl.CanonicalWatchUrl);
        shell.Status = await playbackService.PlayAsync(new PlaybackRequest(video)).ConfigureAwait(false);
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