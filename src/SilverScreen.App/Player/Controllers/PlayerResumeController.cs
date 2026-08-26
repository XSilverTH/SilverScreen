using Gtk;
using SilverScreen.Core.Player;
using static GLib.Functions;

namespace SilverScreen.Player.Controllers;

/// <summary>
///     Lightweight presentation controller that binds UI resume/restart prompt revealers
///     and buttons to the underlying <see cref="PlaybackSession" /> resume state and events.
/// </summary>
internal sealed class PlayerResumeController : IDisposable
{
    private const uint PromptDurationMilliseconds = PlayerTimelineEngine.DefaultResumePromptDurationMilliseconds;
    private readonly Button _restartButton;
    private readonly Label _restartLabel;
    private readonly Revealer _restartRevealer;
    private readonly Button _resumeButton;
    private readonly Label _resumeLabel;
    private readonly Revealer _resumeRevealer;
    private readonly PlaybackSession _session;
    private bool _disposed;
    private uint _promptHideSource;

    public PlayerResumeController(
        PlaybackSession session,
        Revealer resumeRevealer,
        Button resumeButton,
        Label resumeLabel,
        Revealer restartRevealer,
        Button restartButton,
        Label restartLabel)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _resumeRevealer = resumeRevealer;
        _resumeButton = resumeButton;
        _resumeLabel = resumeLabel;
        _restartRevealer = restartRevealer;
        _restartButton = restartButton;
        _restartLabel = restartLabel;

        _session.ResumePromptChanged += OnResumePromptChanged;
        _session.SessionEnded += OnSessionEnded;
        _session.Failed += OnSessionFailed;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.ResumePromptChanged -= OnResumePromptChanged;
        _session.SessionEnded -= OnSessionEnded;
        _session.Failed -= OnSessionFailed;
        HidePrompt();
    }

    private void OnResumePromptChanged(ResumePromptMode mode, TimeSpan position)
    {
        IdleAdd(0, () =>
        {
            if (_disposed) return false;

            switch (mode)
            {
                case ResumePromptMode.Resume:
                    ShowResumePrompt(position);
                    break;
                case ResumePromptMode.Restart:
                    ShowRestartPrompt();
                    break;
                case ResumePromptMode.None:
                default:
                    HidePrompt();
                    break;
            }

            return false;
        });
    }

    private void OnSessionEnded()
    {
        IdleAdd(0, () =>
        {
            if (_disposed) return false;
            HidePrompt();
            return false;
        });
    }

    private void OnSessionFailed(string detail)
    {
        IdleAdd(0, () =>
        {
            if (_disposed) return false;
            HidePrompt();
            return false;
        });
    }

    private void ShowResumePrompt(TimeSpan resumePosition)
    {
        _resumeLabel.SetText($"Resume from {PlayerTimelineEngine.FormatTime(resumePosition)}");
        _resumeButton.SetTooltipText($"Resume playback at {PlayerTimelineEngine.FormatTime(resumePosition)} (Enter)");
        _resumeRevealer.RevealChild = true;
        _restartRevealer.RevealChild = false;
        SchedulePromptHide();
    }

    private void ShowRestartPrompt()
    {
        _restartLabel.SetText("Restart from beginning");
        _restartButton.SetTooltipText("Seek back to 0:00");
        _restartRevealer.RevealChild = true;
        _resumeRevealer.RevealChild = false;
        SchedulePromptHide();
    }

    private void SchedulePromptHide()
    {
        if (_promptHideSource != 0) SourceRemove(_promptHideSource);
        _promptHideSource = TimeoutAdd(0, PromptDurationMilliseconds, () =>
        {
            _promptHideSource = 0;
            if (_disposed) return false;
            _resumeRevealer.RevealChild = false;
            _restartRevealer.RevealChild = false;
            _session.DismissResumePrompt();

            return false;
        });
    }

    private void HidePrompt()
    {
        if (_promptHideSource != 0)
        {
            SourceRemove(_promptHideSource);
            _promptHideSource = 0;
        }

        if (_disposed) return;
        _resumeRevealer.RevealChild = false;
        _restartRevealer.RevealChild = false;
    }
}