using System.Diagnostics.CodeAnalysis;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Player;

namespace SilverScreen.Player.Controllers;

public enum ResumePromptState
{
    None,
    AutoResume,
    ManualResume
}

public sealed class PlayerTimelineEngine(
    uint seekThrottleIntervalMs = PlayerTimelineEngine.DefaultSeekThrottleIntervalMilliseconds,
    long reconciliationLatchMs = PlayerTimelineEngine.DefaultReconciliationLatchMilliseconds,
    double seekToleranceSeconds = PlayerTimelineEngine.DefaultSeekReconciliationToleranceSeconds,
    Func<long>? tickCountProvider = null)
{
    private const uint DefaultSeekThrottleIntervalMilliseconds = 120;
    private const long DefaultReconciliationLatchMilliseconds = 400;
    private const double DefaultSeekReconciliationToleranceSeconds = 1.5;
    private const double MinimumResumeSeconds = 5;
    public const uint DefaultSkipPromptDurationMilliseconds = 5_000;
    public const uint DefaultResumePromptDurationMilliseconds = 15_000;

    private readonly Func<long> _getTickCount = tickCountProvider ?? (() => Environment.TickCount64);

    public bool IsScrubbing { get; private set; }
    public TimeSpan PlaybackPosition { get; private set; }
    public TimeSpan Duration { get; private set; }
    public IReadOnlyList<LibMpvChapter> Chapters { get; private set; } = [];
    public bool HasMedia { get; private set; }
    public TimeSpan ScrubStartPosition { get; private set; }
    public double LatestScrubPositionSeconds { get; private set; }
    public double PendingSeekTargetSeconds { get; private set; } = -1;
    public long ReconciliationLatchExpiry { get; private set; }

    public long LastThrottledSeekTime { get; private set; }

    // --- Timeline State Management & Seeking Reconciliation ---

    public void SetDuration(TimeSpan duration)
    {
        Duration = duration;
    }

    public void SetChapters(IReadOnlyList<LibMpvChapter> chapters)
    {
        Chapters = chapters;
    }

    public void SetPositionDirect(TimeSpan position)
    {
        PlaybackPosition = position;
    }

    public bool UpdatePlaybackState(
        bool hasMedia,
        TimeSpan position,
        TimeSpan duration,
        IReadOnlyList<LibMpvChapter> chapters,
        out bool positionAccepted)
    {
        HasMedia = hasMedia;
        Duration = duration;
        Chapters = chapters;

        if (IsScrubbing)
        {
            positionAccepted = false;
            return false;
        }

        var now = _getTickCount();
        var withinLatch = now < ReconciliationLatchExpiry;
        var isCloseToPending = PendingSeekTargetSeconds >= 0 &&
                               Math.Abs(position.TotalSeconds - PendingSeekTargetSeconds) <= seekToleranceSeconds;

        if (withinLatch && !isCloseToPending)
        {
            positionAccepted = false;
            return false;
        }

        if (isCloseToPending)
        {
            ReconciliationLatchExpiry = 0;
            PendingSeekTargetSeconds = -1;
        }

        PlaybackPosition = position;
        positionAccepted = true;
        return true;
    }

    public void RegisterSeek(double targetSeconds)
    {
        PendingSeekTargetSeconds = targetSeconds;
        ReconciliationLatchExpiry = _getTickCount() + reconciliationLatchMs;
    }

    public void Reset()
    {
        IsScrubbing = false;
        PlaybackPosition = TimeSpan.Zero;
        Duration = TimeSpan.Zero;
        Chapters = [];
        HasMedia = false;
        ScrubStartPosition = TimeSpan.Zero;
        LatestScrubPositionSeconds = 0;
        PendingSeekTargetSeconds = -1;
        ReconciliationLatchExpiry = 0;
        LastThrottledSeekTime = 0;
    }

    // --- Scrubbing Lifecycle ---

    public void BeginScrub(double initialTimelineValue)
    {
        IsScrubbing = true;
        ScrubStartPosition = PlaybackPosition;
        LatestScrubPositionSeconds = initialTimelineValue;
    }

    public void UpdateScrub(double targetSeconds)
    {
        LatestScrubPositionSeconds = targetSeconds;
    }

    public TimeSpan CalculateScrubDelta(TimeSpan targetTime)
    {
        return targetTime - ScrubStartPosition;
    }

    public TimeSpan CancelScrub()
    {
        if (!IsScrubbing) return PlaybackPosition;
        IsScrubbing = false;
        PlaybackPosition = ScrubStartPosition;
        return ScrubStartPosition;
    }

    public double EndScrub(double finalTimelineValue)
    {
        IsScrubbing = false;
        RegisterSeek(finalTimelineValue);
        PlaybackPosition = TimeSpan.FromSeconds(finalTimelineValue);
        return finalTimelineValue;
    }

    public bool ShouldDispatchThrottledSeek(out uint delayMilliseconds)
    {
        var now = _getTickCount();
        var elapsed = now - LastThrottledSeekTime;
        if (elapsed >= seekThrottleIntervalMs)
        {
            LastThrottledSeekTime = now;
            delayMilliseconds = 0;
            return true;
        }

        delayMilliseconds = Math.Max(10u, (uint)(seekThrottleIntervalMs - elapsed));
        return false;
    }

    public void RecordThrottledSeekDispatched()
    {
        LastThrottledSeekTime = _getTickCount();
    }

    // --- Chapter Hit-Testing ---

    public LibMpvChapter? GetChapterAt(TimeSpan position)
    {
        return GetChapterAt(position, Chapters);
    }

    public static LibMpvChapter? GetChapterAt(TimeSpan position, IReadOnlyList<LibMpvChapter> chapters)
    {
        LibMpvChapter? match = null;
        foreach (var chapter in chapters)
            if (chapter.Start <= position)
                match = chapter;
            else
                break;
        return match;
    }

    public static double CalculateChapterMarkerPosition(
        TimeSpan chapterStart,
        TimeSpan duration,
        int trackStart,
        int trackWidth,
        int hostWidth,
        int markerWidth = 20)
    {
        var trackPos = PlayerTimelineGeometry.GetTrackPosition(chapterStart, duration, trackStart, trackWidth);
        return Math.Clamp(Math.Round(trackPos - markerWidth / 2d), 0, Math.Max(0, hostWidth - markerWidth));
    }

    // --- Scrub Cue Badge Geometry ---

    public static double CalculateScrubCueBadgePosition(
        double pointerX,
        double cueWidth,
        double hostWidth,
        double margin = 8.0)
    {
        if (cueWidth <= 0) cueWidth = 80;
        return Math.Clamp(pointerX - cueWidth / 2d, margin, Math.Max(margin, hostWidth - cueWidth - margin));
    }

    // --- Time Formatting & Progress Fraction Math ---

    public static string FormatTime(TimeSpan value)
    {
        var seconds = Math.Max(0, (long)Math.Floor(value.TotalSeconds));
        var duration = TimeSpan.FromSeconds(seconds);
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{duration.Minutes}:{duration.Seconds:D2}";
    }

    public static string FormatDelta(TimeSpan delta)
    {
        var sign = delta < TimeSpan.Zero ? "-" : "+";
        var abs = delta.Duration();
        return abs.TotalHours >= 1
            ? $"{sign}{(int)abs.TotalHours}:{abs.Minutes:D2}:{abs.Seconds:D2}"
            : $"{sign}{(int)abs.TotalMinutes}:{abs.Seconds:D2}";
    }

    public static string FormatDurationLabel(TimeSpan duration)
    {
        return duration <= TimeSpan.Zero ? "Live" : FormatTime(duration);
    }

    public static double CalculateProgressFraction(TimeSpan position, TimeSpan duration)
    {
        return duration <= TimeSpan.Zero ? 0.0 : Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0.0, 1.0);
    }

    // --- SponsorBlock Evaluation ---

    public static SponsorBlockSegment? FindSponsorBlockSegmentAt(
        IReadOnlyList<SponsorBlockSegment> segments,
        TimeSpan position)
    {
        return segments.FirstOrDefault(segment => position >= segment.Start && position < segment.End);
    }

    public static bool ShouldAutoSkip(
        TimeSpan currentPosition,
        IReadOnlyList<SponsorBlockSegment> segments,
        bool isPaused,
        bool autoSkipEnabled,
        ISet<string> autoSkippedSegmentIds,
        [NotNullWhen(true)] out SponsorBlockSegment? segmentToSkip)
    {
        segmentToSkip = null;
        if (isPaused || !autoSkipEnabled || segments.Count == 0) return false;
        var segment = FindSponsorBlockSegmentAt(segments, currentPosition);
        if (segment is null || !autoSkippedSegmentIds.Add(segment.Id)) return false;
        segmentToSkip = segment;
        return true;
    }

    public static bool ShouldShowManualPrompt(
        SponsorBlockSegment? activeSegment,
        SponsorBlockSegment? candidateSegment,
        bool isPaused,
        bool wasPaused,
        bool hadSeek)
    {
        if (candidateSegment is null) return false;
        return hadSeek ||
               !string.Equals(activeSegment?.Id, candidateSegment.Id, StringComparison.Ordinal) ||
               (isPaused && !wasPaused);
    }

    public static bool ManualSponsorBlockSkipEnabled(AppPreferences preferences)
    {
        return ManualSponsorBlockSkipEnabled(preferences.SponsorBlockSegmentDisplayEnabled,
            preferences.SponsorBlockAutoSkipEnabled);
    }

    private static bool ManualSponsorBlockSkipEnabled(bool segmentDisplayEnabled, bool autoSkipEnabled)
    {
        return segmentDisplayEnabled && !autoSkipEnabled;
    }

    public static string GetSponsorBlockCategoryLabel(string category)
    {
        return category switch
        {
            SponsorBlockCategories.Sponsor => "Sponsor",
            SponsorBlockCategories.SelfPromotion => "Self-promotion",
            SponsorBlockCategories.InteractionReminder => "Interaction reminder",
            SponsorBlockCategories.Intro => "Intro",
            SponsorBlockCategories.Outro => "Outro",
            SponsorBlockCategories.Preview => "Preview",
            SponsorBlockCategories.Hook => "Hook",
            SponsorBlockCategories.Filler => "Filler",
            _ => category
        };
    }

    public static string GetSponsorBlockButtonColorClass(string category)
    {
        var resolved = SponsorBlockCategories.All.Contains(category) ? category : SponsorBlockCategories.Sponsor;
        return $"player-sponsorblock-skip-button-{resolved}";
    }

    public static string GetSponsorBlockConfigurationKey(AppPreferences preferences)
    {
        return GetSponsorBlockConfigurationKey(
            preferences.SponsorBlockAutoSkipEnabled,
            preferences.SponsorBlockSegmentDisplayEnabled,
            preferences.SponsorBlockCategories);
    }

    public static string GetSponsorBlockConfigurationKey(
        bool autoSkipEnabled,
        bool segmentDisplayEnabled,
        IEnumerable<string> categories)
    {
        if (!autoSkipEnabled && !segmentDisplayEnabled) return "disabled";
        var filtered = categories.Where(SponsorBlockCategories.All.Contains).Distinct(StringComparer.Ordinal);
        return $"{autoSkipEnabled}:{segmentDisplayEnabled}:{string.Join(',', filtered)}";
    }

    // --- Resume State Evaluation ---

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