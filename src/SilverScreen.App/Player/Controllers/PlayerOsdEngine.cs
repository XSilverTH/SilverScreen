using System.Globalization;

namespace SilverScreen.Player.Controllers;

public enum OsdActionKind
{
    None,
    PlayPause,
    Volume,
    Speed,
    Seek,
    SeekToBeginning,
    Subtitles,
    Queue,
    VideoInfo,
    Fullscreen,
    NextVideo,
    PreviousVideo,
    Resume,
    SkipSponsor
}

public sealed record OsdDisplayModel(string IconName, string Text);

public sealed class PlayerOsdEngine(
    uint aggregationWindowMilliseconds = PlayerOsdEngine.DefaultAggregationWindowMilliseconds,
    Func<long>? tickCountProvider = null)
{
    public const uint DefaultAggregationWindowMilliseconds = 600;
    public const uint DefaultHoldDurationMilliseconds = 700;

    private readonly Func<long> _getTickCount = tickCountProvider ?? (() => Environment.TickCount64);

    public OsdActionKind CurrentActionKind { get; private set; } = OsdActionKind.None;
    public int AccumulatedSeekDeltaSeconds { get; private set; }
    public long LastKeypressTimestamp { get; private set; }
    public bool IsActive { get; private set; }

    public OsdDisplayModel ProcessSeek(int deltaSeconds)
    {
        var now = _getTickCount();
        if (CurrentActionKind == OsdActionKind.Seek && (now - LastKeypressTimestamp) <= aggregationWindowMilliseconds)
        {
            AccumulatedSeekDeltaSeconds += deltaSeconds;
        }
        else
        {
            CurrentActionKind = OsdActionKind.Seek;
            AccumulatedSeekDeltaSeconds = deltaSeconds;
        }

        LastKeypressTimestamp = now;
        IsActive = true;

        var icon = AccumulatedSeekDeltaSeconds >= 0
            ? "media-seek-forward-symbolic"
            : "media-seek-backward-symbolic";
        var text = FormatSeekDelta(AccumulatedSeekDeltaSeconds);

        return new OsdDisplayModel(icon, text);
    }

    public OsdDisplayModel ProcessVolume(double volume, bool isMuted)
    {
        RecordAction(OsdActionKind.Volume);
        var icon = GetVolumeIcon(volume, isMuted);
        var text = isMuted ? "Muted" : $"{Math.Clamp((int)Math.Round(volume), 0, 100)}%";
        return new OsdDisplayModel(icon, text);
    }

    public OsdDisplayModel ProcessPlayPause(bool isPaused)
    {
        RecordAction(OsdActionKind.PlayPause);
        var icon = isPaused ? "media-playback-pause-symbolic" : "media-playback-start-symbolic";
        var text = isPaused ? "Paused" : "Playing";
        return new OsdDisplayModel(icon, text);
    }

    public OsdDisplayModel ProcessSpeed(double speed)
    {
        RecordAction(OsdActionKind.Speed);
        var text = FormatSpeed(speed);
        return new OsdDisplayModel("speedometer-symbolic", text);
    }

    public OsdDisplayModel ProcessSeekToBeginning()
    {
        RecordAction(OsdActionKind.SeekToBeginning);
        return new OsdDisplayModel("media-skip-backward-symbolic", "Beginning");
    }

    public OsdDisplayModel ProcessSubtitles(string trackOrOff)
    {
        RecordAction(OsdActionKind.Subtitles);
        return new OsdDisplayModel("subtitles-symbolic", string.IsNullOrWhiteSpace(trackOrOff) ? "Off" : trackOrOff);
    }

    public OsdDisplayModel ProcessQueue(bool isOpen)
    {
        RecordAction(OsdActionKind.Queue);
        return new OsdDisplayModel("view-list-symbolic", isOpen ? "Queue Open" : "Queue Closed");
    }

    public OsdDisplayModel ProcessVideoInfo(bool isOpen)
    {
        RecordAction(OsdActionKind.VideoInfo);
        return new OsdDisplayModel("info-symbolic", isOpen ? "Video Info Open" : "Video Info Closed");
    }

    public OsdDisplayModel ProcessFullscreen(bool isFullscreen)
    {
        RecordAction(OsdActionKind.Fullscreen);
        return new OsdDisplayModel(
            isFullscreen ? "view-fullscreen-symbolic" : "view-restore-symbolic",
            isFullscreen ? "Fullscreen" : "Exit Fullscreen");
    }

    public OsdDisplayModel ProcessNextVideo()
    {
        RecordAction(OsdActionKind.NextVideo);
        return new OsdDisplayModel("media-skip-forward-symbolic", "Next Video");
    }

    public OsdDisplayModel ProcessPreviousVideo()
    {
        RecordAction(OsdActionKind.PreviousVideo);
        return new OsdDisplayModel("media-skip-backward-symbolic", "Previous Video");
    }

    public OsdDisplayModel ProcessResumed()
    {
        RecordAction(OsdActionKind.Resume);
        return new OsdDisplayModel("media-playback-start-symbolic", "Resumed");
    }

    public OsdDisplayModel ProcessSkippedSponsor()
    {
        RecordAction(OsdActionKind.SkipSponsor);
        return new OsdDisplayModel("media-seek-forward-symbolic", "Skipped Sponsor");
    }

    public void Reset()
    {
        CurrentActionKind = OsdActionKind.None;
        AccumulatedSeekDeltaSeconds = 0;
        IsActive = false;
    }

    private void RecordAction(OsdActionKind kind)
    {
        CurrentActionKind = kind;
        AccumulatedSeekDeltaSeconds = 0;
        LastKeypressTimestamp = _getTickCount();
        IsActive = true;
    }

    public static string FormatSeekDelta(int totalSeconds)
    {
        if (totalSeconds == 0)
            return "0s";

        var sign = totalSeconds > 0 ? "+" : "-";
        var abs = Math.Abs(totalSeconds);
        if (abs < 60)
            return $"{sign}{abs}s";

        var minutes = abs / 60;
        var seconds = abs % 60;
        return seconds == 0 ? $"{sign}{minutes}m" : $"{sign}{minutes}m {seconds}s";
    }

    public static string FormatSpeed(double speed)
    {
        return $"{speed.ToString("0.##", CultureInfo.InvariantCulture)}×";
    }

    public static string GetVolumeIcon(double volume, bool isMuted)
    {
        if (isMuted || volume <= 0) return "audio-volume-muted-symbolic";
        if (volume <= 33) return "audio-volume-low-symbolic";
        if (volume <= 66) return "audio-volume-medium-symbolic";
        return "audio-volume-high-symbolic";
    }
}
