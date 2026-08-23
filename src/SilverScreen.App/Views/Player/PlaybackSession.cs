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
    private bool _disposed;
    private IYouTubePlaybackTelemetrySession? _playbackTelemetrySession;

    public PlaybackRequest? Request { get; private set; }

    public VideoSummary? CurrentVideo { get; private set; }

    public int CurrentPlaylistIndex { get; private set; } = -1;

    public string? CookieFilePath => _cookieFile?.Path;
    public bool HasMedia { get; private set; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Reset();
    }

    public event Action<VideoSummary, int>? VideoChanged;
    public event Action? SessionEnded;
    public event Action<string>? Failed;

    public void Start(PlaybackRequest request)
    {
        if (_disposed) return;
        ReleaseResources();
        Request = request;
        CurrentPlaylistIndex = 0;
        CurrentVideo = request.Videos.Length > 0 ? request.Videos[0] : null;
        HasMedia = false;
        _playbackTelemetrySession = playbackTelemetry.Start(request);
        _cookieFile = cookieFiles.CreateCookieFile();

        if (CurrentVideo is not null) VideoChanged?.Invoke(CurrentVideo, 0);
    }

    public void UpdatePlayback(LibMpvPlaybackState state)
    {
        if (_disposed) return;
        HasMedia = state.HasMedia;

        if (Request is { } playbackRequest && state.HasMedia)
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

        desktopMedia.UpdatePlayback(Request, state);

        if (Request is { } request &&
            state.PlaylistIndex >= 0 &&
            state.PlaylistIndex < int.MaxValue &&
            state.PlaylistIndex < request.Videos.Length)
        {
            var newIndex = state.PlaylistIndex;
            var video = request.Videos[newIndex];
            var videoChanged = CurrentPlaylistIndex != newIndex || CurrentVideo?.Id != video.Id;

            CurrentPlaylistIndex = newIndex;
            CurrentVideo = video;

            if (videoChanged) VideoChanged?.Invoke(video, newIndex);
        }
    }

    public void UpdateQueue(ImmutableArray<VideoSummary> newVideos)
    {
        if (_disposed || Request is null) return;
        Request = new PlaybackRequest(newVideos);
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
        Request = null;
        CurrentVideo = null;
        CurrentPlaylistIndex = -1;
        HasMedia = false;
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
}