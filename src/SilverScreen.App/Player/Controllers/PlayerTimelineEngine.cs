using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Player;
using System.Diagnostics.CodeAnalysis;

namespace SilverScreen.Player.Controllers;

public enum ResumePromptState
{
    None,
    AutoResume,
    ManualResume
}

public sealed class PlayerTimelineEngine
{
    public const uint DefaultSeekThrottleIntervalMilliseconds = 120;
    public const long DefaultReconciliationLatchMilliseconds = 400;
    public const double DefaultSeekReconciliationToleranceSeconds = 1.5;
    public const double MinimumResumeSeconds = 5;
    public const uint DefaultSkipPromptDurationMilliseconds = 5_000;
    public const uint DefaultResumePromptDurationMilliseconds = 15_000;

    private readonly uint _seekThrottleIntervalMs;
    private readonly long _reconciliationLatchMs;
    private readonly double _seekToleranceSeconds;
    private readonly Func<long> _getTickCount;

    private long _reconciliationLatchExpiry;
    private long _lastThrottledSeekTime;

    public PlayerTimelineEngine(
        uint seekThrottleIntervalMs = DefaultSeekThrottleIntervalMilliseconds,
        long reconciliationLatchMs = DefaultReconciliationLatchMilliseconds,
        double seekToleranceSeconds = DefaultSeekReconciliationToleranceSeconds,
        Func<long>? tickCountProvider = null)
    {
        _seekThrottleIntervalMs = seekThrottleIntervalMs;
        _reconciliationLatchMs = reconciliationLatchMs;
        _seekToleranceSeconds = seekToleranceSeconds;
        _getTickCount = tickCountProvider ?? (() => Environment.TickCount64);
    }

    public bool IsScrubbing { get; private set; }
    public TimeSpan PlaybackPosition { get; private set; }
    public TimeSpan Duration { get; private set; }
    public IReadOnlyList<LibMpvChapter> Chapters { get; private set; } = [];
    public bool HasMedia { get; private set; }
    public TimeSpan ScrubStartPosition { get; private set; }
    public double LatestScrubPositionSeconds { get; private set; }
    public double PendingSeekTargetSeconds { get; private set; } = -1;
    public long ReconciliationLatchExpiry => _reconciliationLatchExpiry;
    public long LastThrottledSeekTime => _lastThrottledSeekTime;

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
        var withinLatch = now < _reconciliationLatchExpiry;
        var isCloseToPending = PendingSeekTargetSeconds >= 0 &&
                               Math.Abs(position.TotalSeconds - PendingSeekTargetSeconds) <= _seekToleranceSeconds;

        if (withinLatch && !isCloseToPending)
        {
            positionAccepted = false;
            return false;
        }

        if (isCloseToPending)
        {
            _reconciliationLatchExpiry = 0;
            PendingSeekTargetSeconds = -1;
        }

        PlaybackPosition = position;
        positionAccepted = true;
        return true;
    }

    public void RegisterSeek(double targetSeconds)
    {
        PendingSeekTargetSeconds = targetSeconds;
        _reconciliationLatchExpiry = _getTickCount() + _reconciliationLatchMs;
    }

    public void ClearPendingSeek()
    {
        PendingSeekTargetSeconds = -1;
        _reconciliationLatchExpiry = 0;
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
        _reconciliationLatchExpiry = 0;
        _lastThrottledSeekTime = 0;
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
        var elapsed = now - _lastThrottledSeekTime;
        if (elapsed >= _seekThrottleIntervalMs)
        {
            _lastThrottledSeekTime = now;
            delayMilliseconds = 0;
            return true;
        }

        delayMilliseconds = Math.Max(10u, (uint)(_seekThrottleIntervalMs - elapsed));
        return false;
    }

    public void RecordThrottledSeekDispatched()
    {
        _lastThrottledSeekTime = _getTickCount();
    }

    // --- Chapter Hit-Testing ---

    public LibMpvChapter? GetChapterAt(TimeSpan position) => GetChapterAt(position, Chapters);

    public static LibMpvChapter? GetChapterAt(TimeSpan position, IReadOnlyList<LibMpvChapter> chapters)
    {
        LibMpvChapter? match = null;
        foreach (var chapter in chapters)
        {
            if (chapter.Start <= position)
                match = chapter;
            else
                break;
        }
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
        if (duration <= TimeSpan.Zero) return 0.0;
        return Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0.0, 1.0);
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

    public static bool ManualSponsorBlockSkipEnabled(AppPreferences preferences) =>
        ManualSponsorBlockSkipEnabled(preferences.SponsorBlockSegmentDisplayEnabled, preferences.SponsorBlockAutoSkipEnabled);

    public static bool ManualSponsorBlockSkipEnabled(bool segmentDisplayEnabled, bool autoSkipEnabled)
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

    public static string GetSponsorBlockConfigurationKey(AppPreferences preferences) =>
        GetSponsorBlockConfigurationKey(
            preferences.SponsorBlockAutoSkipEnabled,
            preferences.SponsorBlockSegmentDisplayEnabled,
            preferences.SponsorBlockCategories);

    public static string GetSponsorBlockConfigurationKey(
        bool autoSkipEnabled,
        bool segmentDisplayEnabled,
        IEnumerable<string> categories)
    {
        if (!autoSkipEnabled && !segmentDisplayEnabled) return "disabled";
        var filtered = categories.Where(SponsorBlockCategories.All.Contains).Distinct(StringComparer.Ordinal);
        return $"{autoSkipEnabled}:{segmentDisplayEnabled}:{string.Join(',', filtered)}";
    }

    // --- Resume Threshold Evaluation ---

    public static bool TryGetResumePosition(
        double? fraction,
        TimeSpan duration,
        out TimeSpan position,
        double minimumSeconds = MinimumResumeSeconds)
    {
        position = TimeSpan.Zero;
        if (fraction is not > 0 or >= 1 || duration <= TimeSpan.Zero) return false;
        var candidate = TimeSpan.FromSeconds(duration.TotalSeconds * fraction.Value);
        if (candidate < TimeSpan.FromSeconds(minimumSeconds) || candidate >= duration) return false;
        position = candidate;
        return true;
    }

    public static ResumePromptState GetResumePromptState(
        double? savedFraction,
        TimeSpan duration,
        bool resumeAutomatically,
        bool resumeOnDemand,
        out TimeSpan resumePosition,
        double minimumSeconds = MinimumResumeSeconds)
    {
        if (!TryGetResumePosition(savedFraction, duration, out resumePosition, minimumSeconds))
        {
            return ResumePromptState.None;
        }

        if (resumeAutomatically)
        {
            return ResumePromptState.AutoResume;
        }

        if (resumeOnDemand)
        {
            return ResumePromptState.ManualResume;
        }

        return ResumePromptState.None;
    }
}
