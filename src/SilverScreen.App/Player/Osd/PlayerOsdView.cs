using Gtk;
using SilverScreen.Core.Preferences;
using XSTH.Blueprint.Helpers;
using Object = GObject.Object;

namespace SilverScreen.Player.Osd;

/// <summary>
///     Encapsulates the on-screen display (OSD) notification pill for volume, seek, speed, and state changes.
/// </summary>
public partial class PlayerOsdView : ViewBase<Revealer>
{
    private PlayerOsdController? _osd;

    public void AttachPreferences(IPreferencesService preferences)
    {
        _osd = Lifetime.Own(new PlayerOsdController(preferences, Widget, player_osd_icon, player_osd_label));
    }

    public void ShowPlayPause(bool isPaused) => _osd?.ShowPlayPause(isPaused);
    public void ShowVolume(double volume, bool isMuted) => _osd?.ShowVolume(volume, isMuted);
    public void ShowSpeed(double speed) => _osd?.ShowSpeed(speed);
    public void ShowSeek(int deltaSeconds) => _osd?.ShowSeek(deltaSeconds);
    public void ShowSeekToBeginning() => _osd?.ShowSeekToBeginning();
    public void ShowSubtitles(string state) => _osd?.ShowSubtitles(state);
    public void ShowQueue(bool open) => _osd?.ShowQueue(open);
    public void ShowVideoInfo(bool open) => _osd?.ShowVideoInfo(open);
    public void ShowFullscreen(bool isFullscreen) => _osd?.ShowFullscreen(isFullscreen);
    public void ShowNextVideo() => _osd?.ShowNextVideo();
    public void ShowPreviousVideo() => _osd?.ShowPreviousVideo();
    public void ShowResumed() => _osd?.ShowResumed();
    public void ShowSkippedSponsor() => _osd?.ShowSkippedSponsor();
    public void SetChromeVisible(bool visible) => _osd?.SetChromeVisible(visible);
    public void HideImmediate() => _osd?.HideImmediate();
}
