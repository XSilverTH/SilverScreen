using Adw;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Player.Controls;

/// <summary>
///     Encapsulates the player overlay header bar with back navigation and fullscreen toggle buttons.
/// </summary>
public partial class PlayerHeaderBarView : ViewBase<HeaderBar>
{
    public event EventHandler? BackRequested;
    public event EventHandler? ToggleFullscreenRequested;

    public void SetFullscreen(bool isFullscreen)
    {
        player_fullscreen_button.SetIconName(isFullscreen ? "view-restore-symbolic" : "view-fullscreen-symbolic");
        player_fullscreen_button.SetTooltipText(isFullscreen ? "Exit fullscreen (F / Escape)" : "Toggle fullscreen (F)");
    }

    private void OnBackButtonClicked(object? sender, EventArgs args)
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnFullscreenButtonClicked(object? sender, EventArgs args)
    {
        ToggleFullscreenRequested?.Invoke(this, EventArgs.Empty);
    }
}
