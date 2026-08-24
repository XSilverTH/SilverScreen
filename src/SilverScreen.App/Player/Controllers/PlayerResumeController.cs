using Gtk;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Player;
using static GLib.Functions;

namespace SilverScreen.Player.Controllers;

internal sealed class PlayerResumeController : IDisposable
{
    private const uint PromptDurationMilliseconds = PlayerTimelineEngine.DefaultResumePromptDurationMilliseconds;
    private readonly IPreferencesService _preferences;
    private readonly Revealer _restartRevealer;
    private readonly Button _restartButton;
    private readonly Label _restartLabel;
    private readonly Revealer _resumeRevealer;
    private readonly Button _resumeButton;
    private readonly Label _resumeLabel;
    private readonly Action<double> _seekAbsolute;
    private readonly IWatchProgressService _watchProgress;
    private bool _disposed;
    private bool _handledCurrentVideo;

    private TimeSpan _lastKnownDuration;
    private uint _promptHideSource;
    private double? _resumeFraction;
    private string? _videoId;

    public PlayerResumeController(
        IPreferencesService preferences,
        IWatchProgressService watchProgress,
        Revealer resumeRevealer,
        Button resumeButton,
        Label resumeLabel,
        Revealer restartRevealer,
        Button restartButton,
        Label restartLabel,
        Action<double> seekAbsolute)
    {
        _preferences = preferences;
        _watchProgress = watchProgress;
        _resumeRevealer = resumeRevealer;
        _resumeButton = resumeButton;
        _resumeLabel = resumeLabel;
        _restartRevealer = restartRevealer;
        _restartButton = restartButton;
        _restartLabel = restartLabel;
        _seekAbsolute = seekAbsolute;
        _preferences.PreferencesChanged += OnPreferencesChanged;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _preferences.PreferencesChanged -= OnPreferencesChanged;
        HidePrompt();
    }

    public void Load(VideoSummary video)
    {
        if (_disposed || string.Equals(_videoId, video.Id, StringComparison.Ordinal)) return;
        HidePrompt();
        _videoId = video.Id;
        _resumeFraction = _watchProgress.GetResumeFraction(video.Id);
        _handledCurrentVideo = false;
    }

    public void UpdatePlayback(LibMpvPlaybackState state, string videoId)
    {
        if (_disposed || _handledCurrentVideo || !state.HasMedia ||
            !string.Equals(_videoId, videoId, StringComparison.Ordinal) || state.Duration <= TimeSpan.Zero)
            return;

        _handledCurrentVideo = true;
        _lastKnownDuration = state.Duration;

        var preferences = _preferences.GetPreferences();
        var promptState = PlayerTimelineEngine.GetResumePromptState(
            _resumeFraction,
            state.Duration,
            preferences.ResumePlaybackAutomatically,
            preferences.ResumePlaybackOnDemand,
            out var resumePosition);

        switch (promptState)
        {
            case ResumePromptState.AutoResume:
                _seekAbsolute(resumePosition.TotalSeconds);
                ShowRestartPrompt();
                break;
            case ResumePromptState.ManualResume:
                ShowResumePrompt(resumePosition);
                break;
        }
    }

    public void Clear()
    {
        if (_disposed) return;
        HidePrompt();
        _videoId = null;
        _resumeFraction = null;
        _handledCurrentVideo = false;
        _lastKnownDuration = TimeSpan.Zero;
    }

    public bool TryResume()
    {
        if (_disposed || !_resumeRevealer.RevealChild ||
            !PlayerTimelineEngine.TryGetResumePosition(_resumeFraction, _lastKnownDuration, out var resumePosition))
            return false;

        _seekAbsolute(resumePosition.TotalSeconds);
        HidePrompt();
        return true;
    }

    public bool TryRestart()
    {
        if (_disposed || !_restartRevealer.RevealChild) return false;
        _seekAbsolute(0);
        HidePrompt();
        return true;
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
            if (!_disposed) HidePrompt();
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
        if (!_disposed)
        {
            _resumeRevealer.RevealChild = false;
            _restartRevealer.RevealChild = false;
        }
    }

    private void OnPreferencesChanged(object? sender, AppPreferences preferences)
    {
        IdleAdd(0, () =>
        {
            if (_disposed || preferences.ResumePlaybackAutomatically || preferences.ResumePlaybackOnDemand)
                return false;
            HidePrompt();
            return false;
        });
    }
}
