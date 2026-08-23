using Gtk;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Playback;
using static GLib.Functions;

namespace SilverScreen.Views.Player;

internal sealed class PlayerResumeController : IPlayerFeature
{
    private const uint PromptDurationMilliseconds = 6_000;
    private const double MinimumResumeSeconds = 5;
    private readonly IPreferencesService _preferences;
    private readonly Button _restartButton;
    private readonly Button _resumeButton;
    private readonly Action<double> _seekAbsolute;
    private readonly IWatchProgressService _watchProgress;
    private bool _disposed;
    private bool _handledCurrentVideo;

    private TimeSpan _lastKnownDuration;
    private uint _promptHideSource;
    private double? _resumeFraction;
    private string? _videoId;

    public PlayerResumeController(IPreferencesService preferences, IWatchProgressService watchProgress,
        Button resumeButton, Button restartButton, Action<double> seekAbsolute)
    {
        _preferences = preferences;
        _watchProgress = watchProgress;
        _resumeButton = resumeButton;
        _restartButton = restartButton;
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
        if (!TryGetResumePosition(_resumeFraction, state.Duration, out var resumePosition))
            return;

        var preferences = _preferences.GetPreferences();
        if (preferences.ResumePlaybackAutomatically)
        {
            _seekAbsolute(resumePosition.TotalSeconds);
            ShowRestartPrompt();
        }
        else if (preferences.ResumePlaybackOnDemand)
        {
            ShowResumePrompt(resumePosition);
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
        if (_disposed || !_resumeButton.GetVisible() || _resumeFraction is not { } fraction ||
            !TryGetResumePosition(fraction, _lastKnownDuration, out var resumePosition))
            return false;

        _seekAbsolute(resumePosition.TotalSeconds);
        HidePrompt();
        return true;
    }

    public bool TryRestart()
    {
        if (_disposed || !_restartButton.GetVisible()) return false;
        _seekAbsolute(0);
        HidePrompt();
        return true;
    }

    private static bool TryGetResumePosition(double? fraction, TimeSpan duration, out TimeSpan position)
    {
        position = TimeSpan.Zero;
        if (fraction is not > 0 or >= 1 || duration <= TimeSpan.Zero) return false;
        var candidate = TimeSpan.FromSeconds(duration.TotalSeconds * fraction.Value);
        if (candidate < TimeSpan.FromSeconds(MinimumResumeSeconds) || candidate >= duration) return false;
        position = candidate;
        return true;
    }

    private void ShowResumePrompt(TimeSpan resumePosition)
    {
        _resumeButton.SetLabel($"Resume from {PlayerTimeFormatter.FormatTime(resumePosition)}");
        _resumeButton.SetTooltipText($"Resume playback at {PlayerTimeFormatter.FormatTime(resumePosition)} (Enter)");
        _resumeButton.SetVisible(true);
        _restartButton.SetVisible(false);
        SchedulePromptHide();
    }

    private void ShowRestartPrompt()
    {
        _restartButton.SetLabel("Restart from beginning");
        _restartButton.SetTooltipText("Seek back to 0:00");
        _restartButton.SetVisible(true);
        _resumeButton.SetVisible(false);
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

        _resumeButton.SetVisible(false);
        _restartButton.SetVisible(false);
    }

    private void OnPreferencesChanged(object? sender, AppPreferences preferences)
    {
        IdleAdd(0, () =>
        {
            if (!_disposed) HidePrompt();
            return false;
        });
    }
}