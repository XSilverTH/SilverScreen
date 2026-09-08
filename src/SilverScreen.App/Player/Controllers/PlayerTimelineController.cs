using System.Diagnostics.CodeAnalysis;
using Gtk;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Player;
using Functions = GLib.Functions;

namespace SilverScreen.Player.Controllers;

public enum ResumePromptState
{
    None,
    AutoResume,
    ManualResume
}

/// <summary>
///     Pure timeline/playback-position state: scrubbing lifecycle, seek reconciliation,
///     chapter hit-testing, time formatting, SponsorBlock evaluation, and resume evaluation.
///     Single owner of the logic formerly in <c>PlayerTimelineEngine</c>; composed by
///     <see cref="PlayerTimelineController" /> and subclassed (obsolete) by the compat shim.
/// </summary>
public class PlayerTimelineState(
    uint seekThrottleIntervalMs = PlayerTimelineState.DefaultSeekThrottleIntervalMilliseconds,
    long reconciliationLatchMs = PlayerTimelineState.DefaultReconciliationLatchMilliseconds,
    double seekToleranceSeconds = PlayerTimelineState.DefaultSeekReconciliationToleranceSeconds,
    Func<long>? tickCountProvider = null)
{
    internal const uint DefaultSeekThrottleIntervalMilliseconds = 120;
    internal const long DefaultReconciliationLatchMilliseconds = 400;
    internal const double DefaultSeekReconciliationToleranceSeconds = 1.5;
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

internal sealed class PlayerTimelineController : IDisposable
{
    private readonly Label _durationLabel;
    private readonly Label _positionLabel;
    private readonly Action _registerActivity;
    private readonly Label _scrubChapterLabel;
    private readonly Box _scrubCue;
    private readonly Label _scrubDeltaLabel;
    private readonly Label _scrubTimeLabel;
    private readonly Action<double, bool> _seekAbsolute;

    private readonly Scale _timeline;
    private readonly GestureDrag _timelineDragGesture;
    private readonly EventControllerMotion _timelineMotionController;
    private readonly Overlay _timelineOverlay;

    private bool _disposed;
    private uint _throttledSeekSource;
    private bool _updatingControls;

    public PlayerTimelineController(
        Scale timeline,
        Overlay timelineOverlay,
        Box scrubCue,
        Label scrubTimeLabel,
        Label scrubDeltaLabel,
        Label scrubChapterLabel,
        Label positionLabel,
        Label durationLabel,
        Action<double, bool> seekAbsolute,
        Action registerActivity,
        PlayerTimelineEngine? engine = null)
    {
        _timeline = timeline;
        _timelineOverlay = timelineOverlay;
        _scrubCue = scrubCue;
        _scrubTimeLabel = scrubTimeLabel;
        _scrubDeltaLabel = scrubDeltaLabel;
        _scrubChapterLabel = scrubChapterLabel;
        _positionLabel = positionLabel;
        _durationLabel = durationLabel;
        _seekAbsolute = seekAbsolute;
        _registerActivity = registerActivity;
        State = engine ?? new PlayerTimelineState();

        _timelineMotionController = EventControllerMotion.New();
        _timelineMotionController.OnMotion += OnTimelineMotion;
        _timelineMotionController.OnLeave += OnTimelineLeave;
        ControllerDisposal.Attach(_timelineOverlay, _timelineMotionController);

        _timelineDragGesture = GestureDrag.New();
        _timelineDragGesture.Button = 1;
        _timelineDragGesture.SetPropagationPhase(PropagationPhase.Capture);
        _timelineDragGesture.OnDragBegin += OnTimelineDragBegin;
        _timelineDragGesture.OnDragUpdate += OnTimelineDragUpdate;
        _timelineDragGesture.OnDragEnd += OnTimelineDragEnd;
        ControllerDisposal.Attach(_timeline, _timelineDragGesture);

        _timeline.OnValueChanged += OnTimelineValueChanged;
    }

    public bool IsScrubbing => State.IsScrubbing;

    public TimeSpan PlaybackPosition => State.PlaybackPosition;

    private PlayerTimelineState State { get; }

    public void Dispose()
    {
        if (!ControllerDisposal.TryBeginDispose(ref _disposed)) return;
        CancelThrottledSeek();
        _timelineMotionController.OnMotion -= OnTimelineMotion;
        _timelineMotionController.OnLeave -= OnTimelineLeave;
        ControllerDisposal.Detach(_timelineOverlay, _timelineMotionController);

        _timelineDragGesture.OnDragBegin -= OnTimelineDragBegin;
        _timelineDragGesture.OnDragUpdate -= OnTimelineDragUpdate;
        _timelineDragGesture.OnDragEnd -= OnTimelineDragEnd;
        ControllerDisposal.Detach(_timeline, _timelineDragGesture);

        _timeline.OnValueChanged -= OnTimelineValueChanged;
    }

    public void UpdatePosition(LibMpvPlaybackState state)
    {
        if (_disposed) return;

        _updatingControls = true;
        try
        {
            _durationLabel.SetText(PlayerTimelineState.FormatDurationLabel(state.Duration));
            _timeline.SetRange(0, Math.Max(0, state.Duration.TotalSeconds));
            _timeline.SetSensitive(state.IsSeekable && state.Duration > TimeSpan.Zero);

            if (!State.UpdatePlaybackState(state.HasMedia, state.Position, state.Duration, state.Chapters,
                    out var accepted) || !accepted) return;
            _positionLabel.SetText(PlayerTimelineState.FormatTime(state.Position));
            _timeline.SetValue(Math.Clamp(state.Position.TotalSeconds, 0, Math.Max(0, state.Duration.TotalSeconds)));
        }
        finally
        {
            _updatingControls = false;
        }
    }

    public void SeekAbsolute(double position, bool exact = true)
    {
        State.RegisterSeek(position);
        _seekAbsolute(position, exact);
    }

    public void CancelScrubbing()
    {
        if (!IsScrubbing) return;
        var restoredPosition = State.CancelScrub();
        CancelThrottledSeek();
        _positionLabel.RemoveCssClass("player-time-scrubbing");
        _timeline.RemoveCssClass("dragging");
        _scrubCue.SetVisible(false);
        _updatingControls = true;
        try
        {
            _timeline.SetValue(restoredPosition.TotalSeconds);
            _positionLabel.SetText(PlayerTimelineState.FormatTime(restoredPosition));
        }
        finally
        {
            _updatingControls = false;
        }
    }

    public void Reset()
    {
        CancelScrubbing();
        State.Reset();
        _scrubCue.SetVisible(false);
        _updatingControls = true;
        try
        {
            _timeline.SetRange(0, 0);
            _timeline.SetValue(0);
            _timeline.SetSensitive(false);
            _positionLabel.SetText("0:00");
            _durationLabel.SetText("0:00");
        }
        finally
        {
            _updatingControls = false;
        }
    }

    public void SetDuration(TimeSpan duration)
    {
        State.SetDuration(duration);
        _durationLabel.SetText(PlayerTimelineState.FormatDurationLabel(duration));
    }

    private void OnTimelineMotion(EventControllerMotion sender, EventControllerMotion.MotionSignalArgs args)
    {
        if (_disposed || !State.HasMedia || State.Duration <= TimeSpan.Zero || !_timeline.GetSensitive())
        {
            _scrubCue.SetVisible(false);
            return;
        }

        _registerActivity();
        UpdateScrubCue(args.X);
    }

    private void OnTimelineLeave(object? sender, EventArgs args)
    {
        if (!IsScrubbing)
            _scrubCue.SetVisible(false);
    }

    private void UpdateScrubCue(double pointerX)
    {
        var currentPosition = IsScrubbing ? TimeSpan.FromSeconds(_timeline.GetValue()) : State.PlaybackPosition;
        var (trackStart, trackWidth) = PlayerTimelineGeometry.GetTrack(
            _timeline,
            _timelineOverlay,
            currentPosition,
            State.Duration);

        var targetTime =
            PlayerTimelineGeometry.GetPositionAtCoordinate(pointerX, trackStart, trackWidth, State.Duration);

        var cueWidth = _scrubCue.GetAllocatedWidth();
        var hostWidth = _timelineOverlay.GetAllocatedWidth();
        var badgeX = PlayerTimelineState.CalculateScrubCueBadgePosition(pointerX, cueWidth, hostWidth);
        _scrubCue.MarginStart = (int)Math.Round(badgeX);

        _scrubTimeLabel.SetText(PlayerTimelineState.FormatTime(targetTime));

        if (IsScrubbing)
        {
            var delta = State.CalculateScrubDelta(targetTime);
            _scrubDeltaLabel.SetText(PlayerTimelineState.FormatDelta(delta));
            _scrubDeltaLabel.SetVisible(true);
        }
        else
        {
            _scrubDeltaLabel.SetVisible(false);
        }

        var chapter = State.GetChapterAt(targetTime);
        if (chapter is not null && !string.IsNullOrWhiteSpace(chapter.Title))
        {
            _scrubChapterLabel.SetText(chapter.Title);
            _scrubChapterLabel.SetVisible(true);
        }
        else
        {
            _scrubChapterLabel.SetVisible(false);
        }

        _scrubCue.SetVisible(true);
    }

    private void OnTimelineDragBegin(GestureDrag sender, GestureDrag.DragBeginSignalArgs args)
    {
        if (_disposed || !State.HasMedia || !_timeline.GetSensitive() || State.Duration <= TimeSpan.Zero)
            return;

        State.BeginScrub(_timeline.GetValue());
        _positionLabel.AddCssClass("player-time-scrubbing");
        _timeline.AddCssClass("dragging");
        _registerActivity();
        UpdateScrubCue(args.StartX);
    }

    private void OnTimelineDragUpdate(GestureDrag sender, GestureDrag.DragUpdateSignalArgs args)
    {
        if (!IsScrubbing) return;
        _registerActivity();
        sender.GetStartPoint(out var startX, out _);
        UpdateScrubCue(startX + args.OffsetX);
    }

    private void OnTimelineDragEnd(GestureDrag sender, GestureDrag.DragEndSignalArgs args)
    {
        if (!IsScrubbing) return;
        _positionLabel.RemoveCssClass("player-time-scrubbing");
        _timeline.RemoveCssClass("dragging");
        _scrubCue.SetVisible(false);
        CancelThrottledSeek();

        var finalPosition = _timeline.GetValue();
        State.EndScrub(finalPosition);
        _seekAbsolute(finalPosition, true);
        _positionLabel.SetText(PlayerTimelineState.FormatTime(State.PlaybackPosition));
        _registerActivity();
    }

    private void OnTimelineValueChanged(object? sender, EventArgs args)
    {
        if (_updatingControls || !_timeline.GetSensitive()) return;

        var targetSeconds = _timeline.GetValue();
        State.SetPositionDirect(TimeSpan.FromSeconds(targetSeconds));
        _positionLabel.SetText(PlayerTimelineState.FormatTime(State.PlaybackPosition));

        if (IsScrubbing)
        {
            State.UpdateScrub(targetSeconds);
            if (State.ShouldDispatchThrottledSeek(out var delay) && _throttledSeekSource == 0)
                SeekAbsolute(State.LatestScrubPositionSeconds, false);
            else if (_throttledSeekSource == 0)
                _throttledSeekSource = Functions.TimeoutAdd(0, delay, () =>
                {
                    _throttledSeekSource = 0;
                    if (_disposed || !IsScrubbing) return false;
                    State.RecordThrottledSeekDispatched();
                    SeekAbsolute(State.LatestScrubPositionSeconds, false);
                    return false;
                });
        }
        else
        {
            SeekAbsolute(targetSeconds);
            _registerActivity();
        }
    }

    private void CancelThrottledSeek()
    {
        ControllerDisposal.ClearTimeout(ref _throttledSeekSource);
    }
}
