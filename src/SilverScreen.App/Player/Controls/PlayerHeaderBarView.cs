using Gio;
using Gtk;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Downloads;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Downloads;
using XSTH.Blueprint.Helpers;
using HeaderBar = Adw.HeaderBar;

namespace SilverScreen.Player.Controls;

/// <summary>
///     Encapsulates the player overlay header bar with back navigation, download action, and fullscreen toggle buttons.
/// </summary>
public partial class PlayerHeaderBarView : ViewBase<HeaderBar>
{
    private VideoSummary? _currentVideo;
    private SimpleActionGroup? _downloadActionGroup;
    private PopoverMenu? _downloadPopover;
    private IDownloadService? _downloads;
    private INotificationService? _notifications;
    private IPreferencesService? _preferences;

    public bool IsPopoverOpen => _downloadPopover?.GetVisible() ?? false;

    public event EventHandler? BackRequested;
    public event EventHandler? ToggleFullscreenRequested;

    public void ConfigureDownloads(
        IPreferencesService preferences,
        IDownloadService? downloads,
        INotificationService? notifications)
    {
        _preferences = preferences;
        _downloads = downloads;
        _notifications = notifications;
        UpdateDownloadButtonVisibility();
    }

    public void UpdateCurrentVideo(VideoSummary? video)
    {
        _currentVideo = video;
        UpdateDownloadButtonVisibility();
    }

    public void SetFullscreen(bool isFullscreen)
    {
        player_fullscreen_button.SetIconName(isFullscreen ? "view-restore-symbolic" : "view-fullscreen-symbolic");
        player_fullscreen_button.SetTooltipText(isFullscreen
            ? "Exit fullscreen (F / Escape)"
            : "Toggle fullscreen (F)");
    }

    private void UpdateDownloadButtonVisibility()
    {
        player_download_button.Visible = _downloads is not null &&
                                         _preferences is not null &&
                                         _currentVideo is { Id: { } id } &&
                                         PlaybackRequest.LooksLikeYouTubeVideoId(id);
    }

    private void OnBackButtonClicked(object? sender, EventArgs args)
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnDownloadButtonClicked(object? sender, EventArgs args)
    {
        if (_preferences is null || _downloads is null || _currentVideo is null)
            return;

        DownloadActionHelper.HandleButtonTrigger(
            player_download_button,
            () => _currentVideo is { } v ? [v] : [],
            _preferences,
            _downloads,
            _notifications,
            ref _downloadPopover,
            ref _downloadActionGroup);
    }

    private void OnFullscreenButtonClicked(object? sender, EventArgs args)
    {
        ToggleFullscreenRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_downloadPopover is not null)
            {
                _downloadPopover.Popdown();
                _downloadPopover.Unparent();
                _downloadPopover.Dispose();
                _downloadPopover = null;
            }

            _downloadActionGroup?.Dispose();
            _downloadActionGroup = null;
        }

        base.Dispose(disposing);
    }
}