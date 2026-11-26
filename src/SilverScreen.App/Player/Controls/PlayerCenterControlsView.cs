using Gtk;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Player.Controls;

/// <summary>
///     Encapsulates the center transport controls (rewind, play/pause, and fast forward).
/// </summary>
public partial class PlayerCenterControlsView : ViewBase<Box>
{
    private bool? _isPlaying;
    private bool _isLoading;

    public event EventHandler? RewindRequested;
    public event EventHandler? PlayPauseRequested;
    public event EventHandler? ForwardRequested;

    public void SetPlaybackState(bool isPlaying)
    {
        if (_isPlaying == isPlaying) return;
        _isPlaying = isPlaying;
        player_play_pause_button.SetIconName(isPlaying
            ? "media-playback-pause-symbolic"
            : "media-playback-start-symbolic");
        player_play_pause_button.SetTooltipText(isPlaying
            ? "Pause (Space / K)"
            : "Play (Space / K)");
    }

    public void SetLoading(bool isLoading)
    {
        if (_isLoading == isLoading) return;
        _isLoading = isLoading;
        player_rewind_button.SetVisible(!isLoading);
        player_play_pause_button.SetVisible(!isLoading);
        player_forward_button.SetVisible(!isLoading);
        Widget.SetCanTarget(!isLoading);
    }

    private void OnRewindClicked(object? sender, EventArgs args)
    {
        RewindRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlayPauseClicked(object? sender, EventArgs args)
    {
        PlayPauseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnForwardClicked(object? sender, EventArgs args)
    {
        ForwardRequested?.Invoke(this, EventArgs.Empty);
    }
}