using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Player.Controllers;

public enum ResumePromptState
{
    None,
    AutoResume,
    ManualResume
}

/// <summary>
///     Pure resume state evaluation logic: saved position hit-testing and prompt state calculation.
/// </summary>
public static class PlayerResumeState
{
    public const double MinimumResumeSeconds = 5;
    public const uint DefaultResumePromptDurationMilliseconds = 15_000;

    public static bool TryGetResumePosition(
        YouTubePlaybackProgress? progress,
        TimeSpan duration,
        out TimeSpan position,
        double minimumSeconds = MinimumResumeSeconds)
    {
        position = TimeSpan.Zero;
        if (progress is null || progress.IsCompleted || !progress.HasResumePosition ||
            progress.ResumePosition is not { } savedPosition ||
            savedPosition < TimeSpan.FromSeconds(minimumSeconds) ||
            savedPosition >= duration || duration <= TimeSpan.Zero)
            return false;

        position = savedPosition;
        return true;
    }

    public static ResumePromptState GetResumePromptState(
        YouTubePlaybackProgress? progress,
        TimeSpan duration,
        bool resumeAutomatically,
        bool resumeOnDemand,
        out TimeSpan resumePosition,
        double minimumSeconds = MinimumResumeSeconds)
    {
        if (!TryGetResumePosition(progress, duration, out resumePosition, minimumSeconds))
            return ResumePromptState.None;

        if (resumeAutomatically) return ResumePromptState.AutoResume;

        return resumeOnDemand ? ResumePromptState.ManualResume : ResumePromptState.None;
    }
}
