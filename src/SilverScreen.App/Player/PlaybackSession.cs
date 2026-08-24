using System.Collections.Immutable;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Player;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Shell;

namespace SilverScreen.Player;

internal sealed class PlaybackSession(
    PlaybackCoordinator coordinator,
    DesktopMediaIntegration desktopMedia)
    : IDisposable
{
    private readonly PlaybackCoordinator _coordinator =
        coordinator ?? throw new ArgumentNullException(nameof(coordinator));

    private readonly DesktopMediaIntegration _desktopMedia =
        desktopMedia ?? throw new ArgumentNullException(nameof(desktopMedia));

    private CookieFileLease? _cookieFile;
    private bool _disposed;
    private long _playbackId;

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
        CurrentVideo = PlaybackCoordinator.GetVideoAt(request, 0);
        HasMedia = false;
        _playbackId = _coordinator.RegisterActivePlayback(request);
        _cookieFile = _coordinator.AcquireCookieFileLease();

        if (CurrentVideo is not null) VideoChanged?.Invoke(CurrentVideo, 0);
    }

    public void UpdatePlayback(LibMpvPlaybackState state)
    {
        if (_disposed) return;
        HasMedia = state.HasMedia;

        if (Request is not null && state.HasMedia && _playbackId != 0)
        {
            var playbackState = new PlaybackPresenceState(
                state.PlaylistIndex,
                state.Position,
                state.Duration,
                state.IsPaused,
                state.Speed,
                DateTimeOffset.UtcNow);

            _coordinator.UpdateActivePlayback(_playbackId, playbackState);
        }

        _desktopMedia.UpdatePlayback(Request, state);

        if (!PlaybackCoordinator.TryResolveVideoChange(
                Request,
                CurrentPlaylistIndex,
                CurrentVideo?.Id,
                state.PlaylistIndex,
                out var video,
                out var videoChanged) || video is null) return;
        CurrentPlaylistIndex = state.PlaylistIndex;
        CurrentVideo = video;

        if (!videoChanged) return;
        VideoChanged?.Invoke(video, state.PlaylistIndex);
    }

    public void UpdateQueue(ImmutableArray<VideoSummary> newVideos)
    {
        if (_disposed || Request is null) return;
        Request = PlaybackCoordinator.UpdateQueue(newVideos);
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

    private void Reset()
    {
        ReleaseResources();
        Request = null;
        CurrentVideo = null;
        CurrentPlaylistIndex = -1;
        HasMedia = false;
    }

    private void ReleaseResources()
    {
        if (_playbackId != 0)
        {
            _coordinator.CompleteActivePlayback(_playbackId);
            _playbackId = 0;
        }

        _cookieFile?.Dispose();
        _cookieFile = null;
        _desktopMedia.ClearPlayback();
    }
}