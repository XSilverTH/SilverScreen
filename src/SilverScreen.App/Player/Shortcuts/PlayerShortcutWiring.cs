using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Player;

namespace SilverScreen.Player.Shortcuts;

/// <summary>
///     Binds keyboard shortcut actions to player subsystem components.
/// </summary>
internal static class PlayerShortcutWiring
{
    public static void Wire(
        PlayerShortcutController shortcuts,
        PlayerOverlayComposer composer,
        LibMpvPlayer player,
        PlaybackSession session,
        Action returnToShell,
        Func<bool> toggleFullscreen,
        Action closeComments,
        Func<bool> isCommentsOpen,
        IPreferencesService preferences)
    {
        shortcuts.KeyInterceptor = composer.Stats.HandleKeyPress;

        shortcuts.RegisterAction(PlayerShortcutActions.TogglePause, () =>
        {
            player.TogglePause();
            composer.Osd.ShowPlayPause(!composer.IsPaused);
        });

        shortcuts.RegisterAction(PlayerShortcutActions.SeekBackward, () =>
        {
            player.SeekRelative(-10);
            composer.Osd.ShowSeek(-10);
        });

        shortcuts.RegisterAction(PlayerShortcutActions.SeekForward, () =>
        {
            player.SeekRelative(10);
            composer.Osd.ShowSeek(10);
        });

        shortcuts.RegisterAction(PlayerShortcutActions.StepFrameBackward, () => player.StepFrame(false));
        shortcuts.RegisterAction(PlayerShortcutActions.StepFrameForward, () => player.StepFrame(true));

        shortcuts.RegisterAction(PlayerShortcutActions.ToggleMute, () =>
        {
            player.ToggleMute();
            composer.Osd.ShowVolume(composer.Volume, !composer.IsMuted);
        });

        shortcuts.RegisterAction(PlayerShortcutActions.VolumeUp, () =>
        {
            var vol = Math.Clamp(composer.Volume + 5, 0, 100);
            player.AdjustVolume(5);
            composer.ControlsBar.SetVolume(vol);
            composer.Osd.ShowVolume(vol, false);
        });

        shortcuts.RegisterAction(PlayerShortcutActions.VolumeDown, () =>
        {
            var vol = Math.Clamp(composer.Volume - 5, 0, 100);
            player.AdjustVolume(-5);
            composer.ControlsBar.SetVolume(vol);
            composer.Osd.ShowVolume(vol, false);
        });

        shortcuts.RegisterAction(PlayerShortcutActions.SeekToBeginning, () =>
        {
            composer.Timeline.SeekAbsolute(0);
            composer.Osd.ShowSeekToBeginning();
        });

        shortcuts.RegisterAction(PlayerShortcutActions.ReturnToShell, () =>
        {
            if (composer.Stats.IsOpen) composer.Stats.Close();
            else if (composer.Timeline.IsScrubbing) composer.Timeline.CancelScrubbing();
            else if (composer.ControlsBar.IsQueueActive) composer.ControlsBar.SetQueueActive(false);
            else if (isCommentsOpen()) closeComments();
            else if (composer.InfoPanel.IsOpen) composer.InfoPanel.Close();
            else returnToShell();
        });

        shortcuts.RegisterAction(PlayerShortcutActions.ToggleQueue, () =>
        {
            composer.ControlsBar.ToggleQueue();
            composer.Osd.ShowQueue(composer.ControlsBar.IsQueueActive);
        });

        shortcuts.RegisterAction(PlayerShortcutActions.ToggleVideoInfo, () =>
        {
            composer.InfoPanel.Toggle(session.CurrentVideo);
            composer.Osd.ShowVideoInfo(composer.InfoPanel.IsOpen);
        });

        shortcuts.RegisterAction(PlayerShortcutActions.ToggleStats, composer.Stats.Toggle);

        shortcuts.RegisterAction(PlayerShortcutActions.SpeedDecrease, () =>
        {
            var speed = Math.Clamp(composer.Speed - 0.25, 0.25, 4.0);
            player.SetSpeed(speed);
            composer.ControlsBar.SetSpeed(speed);
            composer.Osd.ShowSpeed(speed);
        });

        shortcuts.RegisterAction(PlayerShortcutActions.SpeedIncrease, () =>
        {
            var speed = Math.Clamp(composer.Speed + 0.25, 0.25, 4.0);
            player.SetSpeed(speed);
            composer.ControlsBar.SetSpeed(speed);
            composer.Osd.ShowSpeed(speed);
        });

        shortcuts.RegisterAction(PlayerShortcutActions.NextVideo, () =>
        {
            player.MovePlaylist(true);
            composer.Osd.ShowNextVideo();
        });

        shortcuts.RegisterAction(PlayerShortcutActions.PreviousVideo, () =>
        {
            player.MovePlaylist(false);
            composer.Osd.ShowPreviousVideo();
        });

        shortcuts.RegisterAction(PlayerShortcutActions.ToggleFullscreen, () =>
        {
            var isFs = toggleFullscreen();
            composer.Osd.ShowFullscreen(isFs);
        });

        shortcuts.RegisterAction(PlayerShortcutActions.PreferredSubtitle, () =>
        {
            var sub = composer.ControlsBar.ShowPreferredSubtitle();
            composer.Osd.ShowSubtitles(sub);
        });

        shortcuts.RegisterAction(PlayerShortcutActions.ResumeOrSkip, () =>
        {
            if (composer.PromptStack.TryConsumeSeekPrompt(out var pos))
            {
                composer.Timeline.SeekAbsolute(pos.TotalSeconds);
                composer.Osd.ShowSeek((int)pos.TotalSeconds);
            }
            else if (session.TryResume())
            {
                composer.Osd.ShowResumed();
            }
            else if (session.TrySkipManualSegment())
            {
                composer.Osd.ShowSkippedSponsor();
            }
        });

        shortcuts.UpdateBindings(preferences.GetPreferences().Shortcuts);
    }
}