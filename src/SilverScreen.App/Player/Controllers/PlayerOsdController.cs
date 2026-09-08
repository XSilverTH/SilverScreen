using System.Globalization;
using Gtk;
using SilverScreen.Core.Preferences;
using static GLib.Functions;

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
    Stats,
    Fullscreen,
    NextVideo,
    PreviousVideo,
    Resume,
    SkipSponsor
}

public sealed record OsdDisplayModel(string IconName, string Text);

/// <summary>
///     Pure OSD aggregation state: coalesces rapid key repeats (seek accumulation) into display models.
///     Composed by <see cref="PlayerOsdController" />.
/// </summary>
public class PlayerOsdState(
    uint aggregationWindowMilliseconds = PlayerOsdState.DefaultAggregationWindowMilliseconds,
    Func<long>? tickCountProvider = null)
{
    internal const uint DefaultAggregationWindowMilliseconds = 600;
    public const uint DefaultHoldDurationMilliseconds = 700;

    private readonly Func<long> _getTickCount = tickCountProvider ?? (() => Environment.TickCount64);

    public OsdActionKind CurrentActionKind { get; private set; } = OsdActionKind.None;
    public int AccumulatedSeekDeltaSeconds { get; private set; }
    private long LastKeypressTimestamp { get; set; }
    public bool IsActive { get; private set; }

    public OsdDisplayModel ProcessSeek(int deltaSeconds)
    {
        var now = _getTickCount();
        if (CurrentActionKind == OsdActionKind.Seek && now - LastKeypressTimestamp <= aggregationWindowMilliseconds)
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

    public OsdDisplayModel ProcessStats(bool isOpen)
    {
        RecordAction(OsdActionKind.Stats);
        return new OsdDisplayModel("utilities-system-monitor-symbolic",
            isOpen ? "Playback Stats: Open" : "Playback Stats: Closed");
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
        return volume switch
        {
            <= 33 => "audio-volume-low-symbolic",
            <= 66 => "audio-volume-medium-symbolic",
            _ => "audio-volume-high-symbolic"
        };
    }
}

internal sealed class PlayerOsdController : IDisposable
{
    private readonly PlayerOsdState _state;
    private readonly uint _holdDurationMilliseconds;
    private readonly Image _osdIcon;
    private readonly Label _osdLabel;
    private readonly Revealer _osdRevealer;
    private readonly IPreferencesService _preferences;
    private bool _disposed;
    private bool _enabled;
    private uint _hideTimeoutSource;

    public PlayerOsdController(
        IPreferencesService preferences,
        Revealer osdRevealer,
        Image osdIcon,
        Label osdLabel,
        PlayerOsdState? state = null,
        uint holdDurationMilliseconds = PlayerOsdState.DefaultHoldDurationMilliseconds)
    {
        _preferences = preferences;
        _osdRevealer = osdRevealer;
        _osdIcon = osdIcon;
        _osdLabel = osdLabel;
        _state = state ?? new PlayerOsdState();
        _holdDurationMilliseconds = holdDurationMilliseconds;

        _enabled = _preferences.GetPreferences().ShortcutOsdEnabled;
        _preferences.PreferencesChanged += OnPreferencesChanged;
    }

    public void Dispose()
    {
        if (!ControllerDisposal.TryBeginDispose(ref _disposed)) return;
        _preferences.PreferencesChanged -= OnPreferencesChanged;
        HideImmediate();
    }

    public void ShowSeek(int deltaSeconds)
    {
        if (!_enabled || _disposed) return;
        var model = _state.ProcessSeek(deltaSeconds);
        ApplyAndScheduleHide(model);
    }

    public void ShowVolume(double volume, bool isMuted)
    {
        if (!_enabled || _disposed) return;
        var model = _state.ProcessVolume(volume, isMuted);
        ApplyAndScheduleHide(model);
    }

    public void ShowPlayPause(bool isPaused)
    {
        if (!_enabled || _disposed) return;
        var model = _state.ProcessPlayPause(isPaused);
        ApplyAndScheduleHide(model);
    }

    public void ShowSpeed(double speed)
    {
        if (!_enabled || _disposed) return;
        var model = _state.ProcessSpeed(speed);
        ApplyAndScheduleHide(model);
    }

    public void ShowSeekToBeginning()
    {
        if (!_enabled || _disposed) return;
        var model = _state.ProcessSeekToBeginning();
        ApplyAndScheduleHide(model);
    }

    public void ShowSubtitles(string trackOrOff)
    {
        if (!_enabled || _disposed) return;
        var model = _state.ProcessSubtitles(trackOrOff);
        ApplyAndScheduleHide(model);
    }

    public void ShowQueue(bool isOpen)
    {
        if (!_enabled || _disposed) return;
        var model = _state.ProcessQueue(isOpen);
        ApplyAndScheduleHide(model);
    }

    public void ShowVideoInfo(bool isOpen)
    {
        if (!_enabled || _disposed) return;
        var model = _state.ProcessVideoInfo(isOpen);
        ApplyAndScheduleHide(model);
    }


    public void ShowFullscreen(bool isFullscreen)
    {
        if (!_enabled || _disposed) return;
        var model = _state.ProcessFullscreen(isFullscreen);
        ApplyAndScheduleHide(model);
    }

    public void ShowNextVideo()
    {
        if (!_enabled || _disposed) return;
        var model = _state.ProcessNextVideo();
        ApplyAndScheduleHide(model);
    }

    public void ShowPreviousVideo()
    {
        if (!_enabled || _disposed) return;
        var model = _state.ProcessPreviousVideo();
        ApplyAndScheduleHide(model);
    }

    public void ShowResumed()
    {
        if (!_enabled || _disposed) return;
        var model = _state.ProcessResumed();
        ApplyAndScheduleHide(model);
    }

    public void ShowSkippedSponsor()
    {
        if (!_enabled || _disposed) return;
        var model = _state.ProcessSkippedSponsor();
        ApplyAndScheduleHide(model);
    }

    public void SetChromeVisible(bool visible)
    {
        if (_disposed) return;
        if (visible)
            _osdRevealer.RemoveCssClass("player-osd-chrome-hidden");
        else
            _osdRevealer.AddCssClass("player-osd-chrome-hidden");
    }

    public void HideImmediate()
    {
        ControllerDisposal.ClearTimeout(ref _hideTimeoutSource);

        _osdRevealer.RevealChild = false;
        _state.Reset();
    }

    private void ApplyAndScheduleHide(OsdDisplayModel model)
    {
        _osdIcon.SetFromIconName(model.IconName);
        _osdLabel.SetText(model.Text);
        _osdRevealer.RevealChild = true;

        ControllerDisposal.ClearTimeout(ref _hideTimeoutSource);

        _hideTimeoutSource = TimeoutAdd(0, _holdDurationMilliseconds, () =>
        {
            _hideTimeoutSource = 0;
            if (_disposed) return false;
            _osdRevealer.RevealChild = false;
            _state.Reset();

            return false;
        });
    }

    private void OnPreferencesChanged(object? sender, AppPreferences preferences)
    {
        _enabled = preferences.ShortcutOsdEnabled;
        if (!_enabled) HideImmediate();
    }
}
