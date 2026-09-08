using System.Collections.Immutable;
using Serilog;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Core.Player;

/// <summary>
///     Headless coordinator that unifies the shared playback lifecycle:
///     telemetry sessions, presence pulsing, cookie file leasing, and playlist/queue synchronization.
///     <para>
///         Process registry, not a view lifecycle: the coordinator tracks <em>multiple</em>
///         concurrent requests (backend routing, process lifetimes, telemetry/presence for
///         each active playback id), while <c>PlaybackSession</c> owns a <em>single</em>
///         view lifecycle (one video/view: OSD, resume prompts, engagement, timeline).
///         Sessions must not spawn or manage player processes directly; all process
///         lifetime and backend routing goes through this coordinator. No logic is owned
///         twice: per-video view state lives in the session, multi-request process state
///         lives here.
///     </para>
/// </summary>
public sealed class PlaybackCoordinator(
    ICookieFileProvider? cookieFiles = null,
    IPlaybackPresenceService? playbackPresence = null,
    IYouTubePlaybackTelemetryService? playbackTelemetry = null)
    : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<PlaybackCoordinator>();
    private readonly Dictionary<long, ActivePlayback> _activePlaybacks = [];
    private readonly Lock _lock = new();
    private bool _disposed;
    private long _nextPlaybackId;

    // O(1) latest-presence tracking: playback ids increase monotonically, so the
    // most recently registered id is the presence owner. Maintained under _lock
    // on register (new id wins) and on complete (recomputed only when the latest
    // itself completes). UpdateActivePlayback compares against this field instead
    // of scanning _activePlaybacks.Keys.Max() per update.
    private long _latestPlaybackId;

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var playback in _activePlaybacks.Values)
                TryDisposeTelemetry(playback.Telemetry, playback.Id, "coordinator disposed");
            _activePlaybacks.Clear();
            _latestPlaybackId = 0;
            TryClearPresence("coordinator disposed");
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
            var telemetry = TryStartTelemetry(request, id);
            var playback = new ActivePlayback(id, request, telemetry);
            _activePlaybacks.Add(id, playback);
            _latestPlaybackId = id;
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
            TryUpdateTelemetry(playback.Telemetry, state, playbackId);

            if (playbackId == _latestPlaybackId) TrySetPresence(playback.Request, state, playbackId);
        }
    }

    public void CompleteActivePlayback(long playbackId)
    {
        lock (_lock)
        {
            if (_disposed || !_activePlaybacks.Remove(playbackId, out var completedPlayback)) return;

            var wasMostRecent = playbackId == _latestPlaybackId;
            TryDisposeTelemetry(completedPlayback.Telemetry, playbackId, "playback completed");

            if (!wasMostRecent) return;

            var currentPlayback = _activePlaybacks.Values.MaxBy(playback => playback.Id);
            _latestPlaybackId = currentPlayback?.Id ?? 0;
            if (currentPlayback?.State is { } state)
                TrySetPresence(currentPlayback.Request, state, currentPlayback.Id);
            else
                TryClearPresence($"playback {playbackId} completed");
        }
    }

    public static VideoSummary? GetVideoAt(PlaybackRequest? request, int index)
    {
        if (request is null || index < 0 || index >= request.Videos.Length) return null;
        return request.Videos[index];
    }

    // Coordinator entry guard: null videoId / empty request is a guidance string, never a throw.
    // Returns null when the request is playable; PlayAsync surfaces the message otherwise.

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
        return new PlaybackRequest(newVideos.IsDefault ? ImmutableArray<VideoSummary>.Empty : newVideos);
    }

    private IYouTubePlaybackTelemetrySession? TryStartTelemetry(PlaybackRequest request, long playbackId)
    {
        if (playbackTelemetry is null) return null;
        try
        {
            return playbackTelemetry.Start(request);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to start playback telemetry for playback {PlaybackId}", playbackId);
            return null;
        }
    }

    private void TryUpdateTelemetry(
        IYouTubePlaybackTelemetrySession? telemetry,
        PlaybackPresenceState state,
        long playbackId)
    {
        if (telemetry is null) return;
        try
        {
            telemetry.UpdateState(state);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to update playback telemetry for playback {PlaybackId}", playbackId);
        }
    }

    private void TrySetPresence(PlaybackRequest request, PlaybackPresenceState state, long playbackId)
    {
        if (playbackPresence is null) return;
        try
        {
            playbackPresence.SetPlaybackState(request, state);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to set playback presence for playback {PlaybackId}", playbackId);
        }
    }

    private void TryClearPresence(string reason)
    {
        if (playbackPresence is null) return;
        try
        {
            playbackPresence.Clear();
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to clear playback presence ({Reason})", reason);
        }
    }

    private static void TryDisposeTelemetry(
        IYouTubePlaybackTelemetrySession? telemetry,
        long playbackId,
        string operation)
    {
        if (telemetry is null) return;
        try
        {
            telemetry.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to dispose playback telemetry for playback {PlaybackId} ({Operation})",
                playbackId, operation);
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
