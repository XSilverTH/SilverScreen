using Gtk;
using SilverScreen.Core.Player;
using static GLib.Functions;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Player.Controllers;

/// <summary>
///     Lightweight presentation controller that binds UI resume/restart prompt revealers
///     and buttons to the underlying <see cref="PlaybackSession" /> resume state and events.
/// </summary>
internal sealed class PlayerResumeController : IDisposable
{
    private const uint PromptDurationMilliseconds = PlayerTimelineState.DefaultResumePromptDurationMilliseconds;
    private readonly Button _restartButton;
    private readonly Label _restartLabel;
    private readonly Revealer _restartRevealer;
    private readonly Button _resumeButton;
    private readonly Label _resumeLabel;
    private readonly Revealer _resumeRevealer;
    private readonly PlaybackSession _session;
    private readonly DisposeScope _lifetime = new();
    private uint _promptHideSource;
    private TimeSpan? _seekPromptPosition;

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

        _lifetime.Track(() => _session.ResumePromptChanged += OnResumePromptChanged, () => _session.ResumePromptChanged -= OnResumePromptChanged);
        _lifetime.Track(() => _session.SessionEnded += OnSessionEnded, () => _session.SessionEnded -= OnSessionEnded);
        _lifetime.Track(() => _session.Failed += OnSessionFailed, () => _session.Failed -= OnSessionFailed);
    }

    public void Dispose()
    {
        if (_lifetime.IsDisposed) return;
        _lifetime.Dispose();
        HidePrompt();
    }

    private void OnResumePromptChanged(ResumePromptMode mode, TimeSpan position)
    {
        IdleAdd(0, () =>
        {
            if (_lifetime.IsDisposed) return false;

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
            if (_lifetime.IsDisposed) return false;
            HidePrompt();
            return false;
        });
    }

    private void OnSessionFailed(string detail)
    {
        IdleAdd(0, () =>
        {
            if (_lifetime.IsDisposed) return false;
            HidePrompt();
            return false;
        });
    }
    /// <summary>
    ///     Offers a jump back to <paramref name="returnPosition" /> (where playback was
    ///     before a timestamp link seek) using the resume prompt UI. Consuming the
    ///     prompt seeks back; it is not a repeat of the original jump.
    /// </summary>
    public void ShowSeekPrompt(TimeSpan returnPosition)
    {
        IdleAdd(0, () =>
        {
            if (_lifetime.IsDisposed) return false;
            _seekPromptPosition = returnPosition;
            _resumeLabel.SetText($"Back to {PlayerTimelineState.FormatTime(returnPosition)}");
            _resumeButton.SetTooltipText($"Return to {PlayerTimelineState.FormatTime(returnPosition)} (Enter)");
            _resumeRevealer.RevealChild = true;
            _restartRevealer.RevealChild = false;
            SchedulePromptHide(false);
            return false;
        });
    }

    private void SchedulePromptHide(bool dismissSessionPrompt = true)
    {
        ClearPromptTimeout();
        _promptHideSource = _lifetime.Timeout(PromptDurationMilliseconds, () =>
        {
            _promptHideSource = 0;
            if (_lifetime.IsDisposed) return false;
            _resumeRevealer.RevealChild = false;
            _restartRevealer.RevealChild = false;
            _seekPromptPosition = null;
            if (dismissSessionPrompt)
                _session.DismissResumePrompt();

            return false;
        });
    }

    public bool TryConsumeSeekPrompt(out TimeSpan position)
    {
        if (_seekPromptPosition is { } seekPosition)
        {
            position = seekPosition;
            _seekPromptPosition = null;
            return true;
        }

        position = TimeSpan.Zero;
        return false;
    }

    private void ShowResumePrompt(TimeSpan resumePosition)
    {
        _seekPromptPosition = null;
        _resumeLabel.SetText($"Resume from {PlayerTimelineState.FormatTime(resumePosition)}");
        _resumeButton.SetTooltipText($"Resume playback at {PlayerTimelineState.FormatTime(resumePosition)} (Enter)");
        _resumeRevealer.RevealChild = true;
        _restartRevealer.RevealChild = false;
        SchedulePromptHide();
    }

    private void ShowRestartPrompt()
    {
        _seekPromptPosition = null;
        _restartLabel.SetText("Restart from beginning");
        _restartButton.SetTooltipText("Seek back to 0:00");
        _restartRevealer.RevealChild = true;
        _resumeRevealer.RevealChild = false;
        SchedulePromptHide();
    }

    private void HidePrompt()
    {
        ClearPromptTimeout();

        if (_lifetime.IsDisposed) return;
        _seekPromptPosition = null;
        _resumeRevealer.RevealChild = false;
        _restartRevealer.RevealChild = false;
    }

    private void ClearPromptTimeout()
    {
        if (_promptHideSource == 0) return;
        _lifetime.Cancel(_promptHideSource);
        _promptHideSource = 0;
    }
}