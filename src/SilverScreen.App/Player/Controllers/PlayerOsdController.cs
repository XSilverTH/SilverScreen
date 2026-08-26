using Gtk;
using SilverScreen.Core.Preferences;
using static GLib.Functions;

namespace SilverScreen.Player.Controllers;

internal sealed class PlayerOsdController : IDisposable
{
    private readonly PlayerOsdEngine _engine;
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
        PlayerOsdEngine? engine = null,
        uint holdDurationMilliseconds = PlayerOsdEngine.DefaultHoldDurationMilliseconds)
    {
        _preferences = preferences;
        _osdRevealer = osdRevealer;
        _osdIcon = osdIcon;
        _osdLabel = osdLabel;
        _engine = engine ?? new PlayerOsdEngine();
        _holdDurationMilliseconds = holdDurationMilliseconds;

        _enabled = _preferences.GetPreferences().ShortcutOsdEnabled;
        _preferences.PreferencesChanged += OnPreferencesChanged;
    }

    public void ShowSeek(int deltaSeconds)
    {
        if (!_enabled || _disposed) return;
        var model = _engine.ProcessSeek(deltaSeconds);
        ApplyAndScheduleHide(model);
    }

    public void ShowVolume(double volume, bool isMuted)
    {
        if (!_enabled || _disposed) return;
        var model = _engine.ProcessVolume(volume, isMuted);
        ApplyAndScheduleHide(model);
    }

    public void ShowPlayPause(bool isPaused)
    {
        if (!_enabled || _disposed) return;
        var model = _engine.ProcessPlayPause(isPaused);
        ApplyAndScheduleHide(model);
    }

    public void ShowSpeed(double speed)
    {
        if (!_enabled || _disposed) return;
        var model = _engine.ProcessSpeed(speed);
        ApplyAndScheduleHide(model);
    }

    public void ShowSeekToBeginning()
    {
        if (!_enabled || _disposed) return;
        var model = _engine.ProcessSeekToBeginning();
        ApplyAndScheduleHide(model);
    }

    public void ShowSubtitles(string trackOrOff)
    {
        if (!_enabled || _disposed) return;
        var model = _engine.ProcessSubtitles(trackOrOff);
        ApplyAndScheduleHide(model);
    }

    public void ShowQueue(bool isOpen)
    {
        if (!_enabled || _disposed) return;
        var model = _engine.ProcessQueue(isOpen);
        ApplyAndScheduleHide(model);
    }

    public void ShowVideoInfo(bool isOpen)
    {
        if (!_enabled || _disposed) return;
        var model = _engine.ProcessVideoInfo(isOpen);
        ApplyAndScheduleHide(model);
    }

    public void ShowFullscreen(bool isFullscreen)
    {
        if (!_enabled || _disposed) return;
        var model = _engine.ProcessFullscreen(isFullscreen);
        ApplyAndScheduleHide(model);
    }

    public void ShowNextVideo()
    {
        if (!_enabled || _disposed) return;
        var model = _engine.ProcessNextVideo();
        ApplyAndScheduleHide(model);
    }

    public void ShowPreviousVideo()
    {
        if (!_enabled || _disposed) return;
        var model = _engine.ProcessPreviousVideo();
        ApplyAndScheduleHide(model);
    }

    public void ShowResumed()
    {
        if (!_enabled || _disposed) return;
        var model = _engine.ProcessResumed();
        ApplyAndScheduleHide(model);
    }

    public void ShowSkippedSponsor()
    {
        if (!_enabled || _disposed) return;
        var model = _engine.ProcessSkippedSponsor();
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
        if (_hideTimeoutSource != 0)
        {
            SourceRemove(_hideTimeoutSource);
            _hideTimeoutSource = 0;
        }

        _osdRevealer.RevealChild = false;
        _engine.Reset();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _preferences.PreferencesChanged -= OnPreferencesChanged;
        HideImmediate();
    }

    private void ApplyAndScheduleHide(OsdDisplayModel model)
    {
        _osdIcon.SetFromIconName(model.IconName);
        _osdLabel.SetText(model.Text);
        _osdRevealer.RevealChild = true;

        if (_hideTimeoutSource != 0)
        {
            SourceRemove(_hideTimeoutSource);
            _hideTimeoutSource = 0;
        }

        _hideTimeoutSource = TimeoutAdd(0, _holdDurationMilliseconds, () =>
        {
            _hideTimeoutSource = 0;
            if (!_disposed)
            {
                _osdRevealer.RevealChild = false;
                _engine.Reset();
            }

            return false;
        });
    }

    private void OnPreferencesChanged(object? sender, AppPreferences preferences)
    {
        _enabled = preferences.ShortcutOsdEnabled;
        if (!_enabled) HideImmediate();
    }
}
