using System.Collections.Immutable;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Core.Player;

/// <summary>
///     Headless coordinator that unifies the shared playback lifecycle:
///     telemetry sessions, presence pulsing, cookie file leasing, and playlist/queue synchronization.
/// </summary>
public sealed class PlaybackCoordinator(
    ICookieFileProvider? cookieFiles = null,
    IPlaybackPresenceService? playbackPresence = null,
    IYouTubePlaybackTelemetryService? playbackTelemetry = null)
    : IDisposable
{
    private readonly Dictionary<long, ActivePlayback> _activePlaybacks = [];
    private readonly Lock _lock = new();
    private bool _disposed;
    private long _nextPlaybackId;

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var playback in _activePlaybacks.Values) playback.Telemetry?.Dispose();
            _activePlaybacks.Clear();
            ClearPresenceQuietly();
        }
    }

    public CookieFileLease? AcquireCookieFileLease()
    {
        return cookieFiles?.CreateCookieFile();
    }

    public long RegisterActivePlayback(PlaybackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_lock)
        {
            if (_disposed) return 0;
            var id = ++_nextPlaybackId;
            var telemetry = StartTelemetryQuietly(request);
            var playback = new ActivePlayback(id, request, telemetry);
            _activePlaybacks.Add(id, playback);
            return id;
        }
    }

    public void UpdateActivePlayback(long playbackId, PlaybackPresenceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_lock)
        {
            if (_disposed || !_activePlaybacks.TryGetValue(playbackId, out var playback)) return;

            playback.State = state;
            SetTelemetryQuietly(playback.Telemetry, state);

            if (_activePlaybacks.Keys.Max() == playbackId) SetPresenceQuietly(playback.Request, state);
        }
    }

    public void CompleteActivePlayback(long playbackId)
    {
        lock (_lock)
        {
            if (_disposed || !_activePlaybacks.Remove(playbackId, out var completedPlayback)) return;

            var wasMostRecent = _activePlaybacks.Count == 0 || _activePlaybacks.Keys.Max() < playbackId;
            completedPlayback.Telemetry?.Dispose();

            if (!wasMostRecent) return;

            var currentPlayback = _activePlaybacks.Values.MaxBy(playback => playback.Id);
            if (currentPlayback?.State is { } state)
                SetPresenceQuietly(currentPlayback.Request, state);
            else
                ClearPresenceQuietly();
        }
    }

    public static VideoSummary? GetVideoAt(PlaybackRequest? request, int index)
    {
        if (request is null || index < 0 || index >= request.Videos.Length) return null;
        return request.Videos[index];
    }

    public static bool TryResolveVideoChange(
        PlaybackRequest? request,
        int currentIndex,
        string? currentVideoId,
        int newIndex,
        out VideoSummary? video,
        out bool videoChanged)
    {
        video = null;
        videoChanged = false;

        if (request is null || newIndex < 0 || newIndex >= request.Videos.Length)
            return false;

        video = request.Videos[newIndex];
        videoChanged = currentIndex != newIndex || !string.Equals(currentVideoId, video.Id, StringComparison.Ordinal);
        return true;
    }

    public static PlaybackRequest UpdateQueue(ImmutableArray<VideoSummary> newVideos)
    {
        return new PlaybackRequest(newVideos);
    }

    private IYouTubePlaybackTelemetrySession? StartTelemetryQuietly(PlaybackRequest request)
    {
        if (playbackTelemetry is null) return null;
        try
        {
            return playbackTelemetry.Start(request);
        }
        catch
        {
            return null;
        }
    }

    private static void SetTelemetryQuietly(IYouTubePlaybackTelemetrySession? telemetry, PlaybackPresenceState state)
    {
        if (telemetry is null) return;
        try
        {
            telemetry.UpdateState(state);
        }
        catch
        {
            // ignored
        }
    }

    private void SetPresenceQuietly(PlaybackRequest request, PlaybackPresenceState state)
    {
        if (playbackPresence is null) return;
        try
        {
            playbackPresence.SetPlaybackState(request, state);
        }
        catch
        {
            // ignored
        }
    }

    private void ClearPresenceQuietly()
    {
        if (playbackPresence is null) return;
        try
        {
            playbackPresence.Clear();
        }
        catch
        {
            // ignored
        }
    }

    private sealed class ActivePlayback(long id, PlaybackRequest request, IYouTubePlaybackTelemetrySession? telemetry)
    {
        public long Id { get; } = id;
        public PlaybackRequest Request { get; } = request;
        public IYouTubePlaybackTelemetrySession? Telemetry { get; } = telemetry;
        public PlaybackPresenceState? State { get; set; }
    }
}