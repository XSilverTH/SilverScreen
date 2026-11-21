using Gtk;
using SilverScreen.Core.Preferences;
using SilverScreen.Player.Controllers;
using XSTH.Blueprint.Helpers;
using Object = GObject.Object;

namespace SilverScreen.Player.Views;

/// <summary>
///     Encapsulates the prompt cards overlay for SponsorBlock skip buttons and resume/restart prompts.
/// </summary>
public partial class PlayerPromptStackView : ViewBase<Box>
{
    private PlaybackSession? _session;
    private PlayerResumeController? _resumeController;
    private PlayerSponsorBlockController? _sponsorBlockController;

    public event EventHandler<TimeSpan>? SeekRequested;

    internal void AttachControllers(
        PlaybackSession session,
        IPreferencesService preferences,
        Scale timeline,
        Overlay timelineOverlay)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));

        _sponsorBlockController = Lifetime.Own(new PlayerSponsorBlockController(
            session,
            preferences,
            timeline,
            timelineOverlay,
            player_sponsorblock_revealer,
            player_sponsorblock_skip_button,
            player_sponsorblock_label));

        _resumeController = Lifetime.Own(new PlayerResumeController(
            session,
            player_resume_revealer,
            player_resume_button,
            player_resume_label,
            player_restart_revealer,
            player_restart_button,
            player_restart_label));

        Lifetime.Track(() => player_sponsorblock_revealer.OnNotify += OnRevealerNotify, () => player_sponsorblock_revealer.OnNotify -= OnRevealerNotify);
        Lifetime.Track(() => player_resume_revealer.OnNotify += OnRevealerNotify, () => player_resume_revealer.OnNotify -= OnRevealerNotify);
        Lifetime.Track(() => player_restart_revealer.OnNotify += OnRevealerNotify, () => player_restart_revealer.OnNotify -= OnRevealerNotify);
    }

    public bool TryConsumeSeekPrompt(out TimeSpan returnPosition)
    {
        if (_resumeController is not null)
            return _resumeController.TryConsumeSeekPrompt(out returnPosition);

        returnPosition = TimeSpan.Zero;
        return false;
    }

    public void SkipActiveSponsorSegment()
    {
        _session?.TrySkipManualSegment();
    }

    public void ConsumeResumePrompt()
    {
        if (_resumeController is not null && _resumeController.TryConsumeSeekPrompt(out var pos))
            SeekRequested?.Invoke(this, pos);
        else
            _session?.TryResume();
    }

    public void ConsumeRestartPrompt()
    {
        _session?.TryRestart();
    }

    private void SyncStackVisibility()
    {
        Widget.SetVisible(
            player_sponsorblock_revealer.GetVisible() ||
            player_resume_revealer.GetVisible() ||
            player_restart_revealer.GetVisible());
    }

    private void OnSponsorBlockSkipButtonClicked(object? sender, EventArgs args) =>
        SkipActiveSponsorSegment();

    private void OnResumeButtonClicked(object? sender, EventArgs args) =>
        ConsumeResumePrompt();

    private void OnRestartButtonClicked(object? sender, EventArgs args) =>
        ConsumeRestartPrompt();

    private void OnRevealerNotify(Object sender, Object.NotifySignalArgs args)
    {
        if (args.Pspec.GetName() == "visible")
            SyncStackVisibility();
    }
}
