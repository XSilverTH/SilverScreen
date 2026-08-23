using System.Collections.Immutable;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Playback;

namespace SilverScreen.Views.Player;

internal sealed class PlaybackSession(
    ICookieFileProvider cookieFiles,
    IPlaybackPresenceService playbackPresence,
    IYouTubePlaybackTelemetryService playbackTelemetry,
    IWatchProgressService watchProgress,
    DesktopMediaIntegration desktopMedia)
    : IDisposable
{
    private CookieFileLease? _cookieFile;
    private int _currentPlaylistIndex = -1;
    private VideoSummary? _currentVideo;
    private bool _disposed;
    private bool _hasMedia;
    private IYouTubePlaybackTelemetrySession? _playbackTelemetrySession;
    private PlaybackRequest? _request;

    public PlaybackRequest? Request => _request;
    public VideoSummary? CurrentVideo => _currentVideo;
    public int CurrentPlaylistIndex => _currentPlaylistIndex;
    public string? CookieFilePath => _cookieFile?.Path;
    public bool HasMedia => _hasMedia;

    public event Action<VideoSummary, int>? VideoChanged;
    public event Action? SessionEnded;
    public event Action<string>? Failed;

    public void Start(PlaybackRequest request)
    {
        if (_disposed) return;
        ReleaseResources();
        _request = request;
        _currentPlaylistIndex = 0;
        _currentVideo = request.Videos.Length > 0 ? request.Videos[0] : null;
        _hasMedia = false;
        _playbackTelemetrySession = playbackTelemetry.Start(request);
        _cookieFile = cookieFiles.CreateCookieFile();

        if (_currentVideo is not null)
        {
            VideoChanged?.Invoke(_currentVideo, 0);
        }
    }

    public void UpdatePlayback(LibMpvPlaybackState state)
    {
        if (_disposed) return;
        _hasMedia = state.HasMedia;

        if (_request is { } playbackRequest && state.HasMedia)
        {
            var playbackState = new PlaybackPresenceState(
                state.PlaylistIndex,
                state.Position,
                state.Duration,
                state.IsPaused,
                state.Speed,
                DateTimeOffset.UtcNow);

            playbackPresence.SetPlaybackState(playbackRequest, playbackState);
            _playbackTelemetrySession?.UpdateState(playbackState);
            watchProgress.Update(playbackRequest, playbackState);
        }

        desktopMedia.UpdatePlayback(_request, state);

        if (_request is { } request &&
            state.PlaylistIndex >= 0 &&
            state.PlaylistIndex < int.MaxValue &&
            state.PlaylistIndex < request.Videos.Length)
        {
            var newIndex = state.PlaylistIndex;
            var video = request.Videos[newIndex];
            var videoChanged = _currentPlaylistIndex != newIndex || _currentVideo?.Id != video.Id;

            _currentPlaylistIndex = newIndex;
            _currentVideo = video;

            if (videoChanged)
            {
                VideoChanged?.Invoke(video, newIndex);
            }
        }
    }

    public void UpdateQueue(ImmutableArray<VideoSummary> newVideos)
    {
        if (_disposed || _request is null) return;
        _request = new PlaybackRequest(newVideos);
    }

    public void EndSession()
    {
        if (_disposed) return;
        Reset();
        SessionEnded?.Invoke();
    }

    public void Fail(string detail)
    {
        if (_disposed) return;
        Reset();
        Failed?.Invoke(detail);
    }

    public void Reset()
    {
        ReleaseResources();
        _request = null;
        _currentVideo = null;
        _currentPlaylistIndex = -1;
        _hasMedia = false;
    }

    private void ReleaseResources()
    {
        playbackPresence.Clear();
        _playbackTelemetrySession?.Dispose();
        _playbackTelemetrySession = null;
        _cookieFile?.Dispose();
        _cookieFile = null;
        desktopMedia.ClearPlayback();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Reset();
    }
}
