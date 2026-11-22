using Gtk;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Preferences;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Shell.Header;

/// <summary>
///     Encapsulates the now-playing header bar controls: play/pause toggle and return-to-player button.
/// </summary>
public partial class NowPlayingHeaderBarView : ViewBase<Box>
{
    private static readonly ILogger Logger = Log.ForContext<NowPlayingHeaderBarView>();
    private readonly INavigationService _navigationService;
    private readonly IPlaybackModeRoutingService _playback;
    private readonly IPreferencesService _preferences;

    public NowPlayingHeaderBarView(
        IPlaybackModeRoutingService playback,
        INavigationService navigationService,
        IPreferencesService preferences)
    {
        _playback = playback ?? throw new ArgumentNullException(nameof(playback));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));

        Lifetime.Track(
            () => _playback.PlaybackStateChanged += OnPlaybackStateChanged,
            () => _playback.PlaybackStateChanged -= OnPlaybackStateChanged);

        Lifetime.Track(
            () => _navigationService.PageChanged += OnPageChanged,
            () => _navigationService.PageChanged -= OnPageChanged);

        UpdateState();
    }

    private void OnNowPlayingToggleClicked(Button sender, EventArgs args)
    {
        _playback.TogglePauseAsync().FireAndForget(Logger);
    }

    private void OnNowPlayingReturnButtonClicked(Button sender, EventArgs args)
    {
        _navigationService.NavigateToPlayer();
        if (_preferences.GetPreferences().OpenInFullscreen && Widget.GetRoot() is Window window)
            window.Fullscreen();
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs e)
    {
        Lifetime.Idle(() =>
        {
            if (!IsDisposed) UpdateState();
            return false;
        });
    }

    private void OnPageChanged(object? sender, NavigationPageChangedEventArgs e)
    {
        Lifetime.Idle(() =>
        {
            if (!IsDisposed) UpdateState();
            return false;
        });
    }

    private void UpdateState()
    {
        var hasMedia = _playback.HasMedia;
        var notInPlayer = _navigationService.CurrentPage != NavigationPage.Player;
        var show = hasMedia && notInPlayer;

        Widget.Visible = show;
        if (!show) return;

        var isPaused = _playback.IsPaused;
        now_playing_toggle.IconName = isPaused
            ? "media-playback-start-symbolic"
            : "media-playback-pause-symbolic";
        now_playing_toggle.TooltipText = isPaused ? "Play" : "Pause";
    }
}