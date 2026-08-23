using Gtk;
using SilverScreen.Infrastructure.Features.Playback;
using Functions = GLib.Functions;

namespace SilverScreen.Views.Player;

internal sealed class PlayerTimelineController : IDisposable
{
    private const uint SeekThrottleIntervalMilliseconds = 120;
    private const long ReconciliationLatchMilliseconds = 400;

    private readonly Scale _timeline;
    private readonly Overlay _timelineOverlay;
    private readonly Box _scrubCue;
    private readonly Label _scrubTimeLabel;
    private readonly Label _scrubDeltaLabel;
    private readonly Label _scrubChapterLabel;
    private readonly Label _positionLabel;
    private readonly Label _durationLabel;
    private readonly Action<double, bool> _seekAbsolute;
    private readonly Action _registerActivity;

    private readonly EventControllerMotion _timelineMotionController;
    private readonly GestureDrag _timelineDragGesture;

    private IReadOnlyList<LibMpvChapter> _chapters = [];
    private TimeSpan _currentDuration;
    private TimeSpan _timelinePlaybackPosition;
    private bool _hasMedia;
    private bool _isScrubbing;
    private TimeSpan _scrubStartPosition;
    private double _latestScrubPosition;
    private double _pendingSeekTarget = -1;
    private long _reconciliationLatchExpiry;
    private long _lastThrottledSeekTime;
    private uint _throttledSeekSource;
    private bool _updatingControls;
    private bool _disposed;

    public bool IsScrubbing => _isScrubbing;
    public TimeSpan PlaybackPosition => _timelinePlaybackPosition;
    public TimeSpan CurrentDuration => _currentDuration;

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
        Action registerActivity)
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

        _timelineMotionController = EventControllerMotion.New();
        _timelineMotionController.OnMotion += OnTimelineMotion;
        _timelineMotionController.OnLeave += OnTimelineLeave;
        _timelineOverlay.AddController(_timelineMotionController);

        _timelineDragGesture = GestureDrag.New();
        _timelineDragGesture.Button = 1;
        _timelineDragGesture.SetPropagationPhase(PropagationPhase.Capture);
        _timelineDragGesture.OnDragBegin += OnTimelineDragBegin;
        _timelineDragGesture.OnDragUpdate += OnTimelineDragUpdate;
        _timelineDragGesture.OnDragEnd += OnTimelineDragEnd;
        _timeline.AddController(_timelineDragGesture);

        _timeline.OnValueChanged += OnTimelineValueChanged;
    }

    public void UpdatePosition(LibMpvPlaybackState state)
    {
        if (_disposed) return;
        _hasMedia = state.HasMedia;
        _currentDuration = state.Duration;
        _chapters = state.Chapters;

        _updatingControls = true;
        try
        {
            _durationLabel.SetText(state.Duration == TimeSpan.Zero ? "Live" : FormatTime(state.Duration));
            _timeline.SetRange(0, Math.Max(0, state.Duration.TotalSeconds));
            _timeline.SetSensitive(state.IsSeekable && state.Duration > TimeSpan.Zero);

            if (!_isScrubbing)
            {
                var withinLatch = Environment.TickCount64 < _reconciliationLatchExpiry;
                var isCloseToPending = Math.Abs(state.Position.TotalSeconds - _pendingSeekTarget) <= 1.5;

                if (!withinLatch || isCloseToPending)
                {
                    if (isCloseToPending) _reconciliationLatchExpiry = 0;
                    _timelinePlaybackPosition = state.Position;
                    _positionLabel.SetText(FormatTime(state.Position));
                    _timeline.SetValue(Math.Clamp(state.Position.TotalSeconds, 0,
                        Math.Max(0, state.Duration.TotalSeconds)));
                }
            }
        }
        finally
        {
            _updatingControls = false;
        }
    }

    public void SeekAbsolute(double position, bool exact = true)
    {
        _pendingSeekTarget = position;
        _reconciliationLatchExpiry = Environment.TickCount64 + ReconciliationLatchMilliseconds;
        _seekAbsolute(position, exact);
    }

    public void CancelScrubbing()
    {
        if (!_isScrubbing) return;
        _isScrubbing = false;
        CancelThrottledSeek();
        _positionLabel.RemoveCssClass("player-time-scrubbing");
        _timeline.RemoveCssClass("dragging");
        _scrubCue.SetVisible(false);
        _updatingControls = true;
        try
        {
            _timeline.SetValue(_scrubStartPosition.TotalSeconds);
            _timelinePlaybackPosition = _scrubStartPosition;
            _positionLabel.SetText(FormatTime(_scrubStartPosition));
        }
        finally
        {
            _updatingControls = false;
        }
    }

    public void Reset()
    {
        CancelScrubbing();
        _currentDuration = TimeSpan.Zero;
        _chapters = [];
        _scrubCue.SetVisible(false);
        _updatingControls = true;
        try
        {
            _timeline.SetRange(0, 0);
            _timeline.SetValue(0);
            _timeline.SetSensitive(false);
            _positionLabel.SetText("0:00");
            _durationLabel.SetText("0:00");
            _timelinePlaybackPosition = TimeSpan.Zero;
        }
        finally
        {
            _updatingControls = false;
        }
    }

    public void SetDuration(TimeSpan duration)
    {
        _currentDuration = duration;
        _durationLabel.SetText(FormatTime(duration));
    }

    private void OnTimelineMotion(EventControllerMotion sender, EventControllerMotion.MotionSignalArgs args)
    {
        if (_disposed || !_hasMedia || _currentDuration <= TimeSpan.Zero || !_timeline.GetSensitive())
        {
            _scrubCue.SetVisible(false);
            return;
        }

        _registerActivity();
        UpdateScrubCue(args.X);
    }

    private void OnTimelineLeave(object? sender, EventArgs args)
    {
        if (!_isScrubbing)
            _scrubCue.SetVisible(false);
    }

    private void UpdateScrubCue(double pointerX)
    {
        var (trackStart, trackWidth) = PlayerTimelineGeometry.GetTrack(_timeline, _timelineOverlay,
            _isScrubbing ? TimeSpan.FromSeconds(_timeline.GetValue()) : _timelinePlaybackPosition,
            _currentDuration);

        var targetTime =
            PlayerTimelineGeometry.GetPositionAtCoordinate(pointerX, trackStart, trackWidth, _currentDuration);

        var cueWidth = _scrubCue.GetAllocatedWidth();
        var hostWidth = _timelineOverlay.GetAllocatedWidth();
        if (cueWidth <= 0) cueWidth = 80;
        var badgeX = Math.Clamp(pointerX - cueWidth / 2d, 8, Math.Max(8, hostWidth - cueWidth - 8));
        _scrubCue.MarginStart = (int)Math.Round(badgeX);

        _scrubTimeLabel.SetText(FormatTime(targetTime));

        if (_isScrubbing)
        {
            var delta = targetTime - _scrubStartPosition;
            _scrubDeltaLabel.SetText(FormatDelta(delta));
            _scrubDeltaLabel.SetVisible(true);
        }
        else
        {
            _scrubDeltaLabel.SetVisible(false);
        }

        var chapter = FindChapterAt(targetTime);
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
        if (_disposed || !_hasMedia || !_timeline.GetSensitive() || _currentDuration <= TimeSpan.Zero)
            return;

        _isScrubbing = true;
        _scrubStartPosition = _timelinePlaybackPosition;
        _latestScrubPosition = _timeline.GetValue();
        _positionLabel.AddCssClass("player-time-scrubbing");
        _timeline.AddCssClass("dragging");
        _registerActivity();
        UpdateScrubCue(args.StartX);
    }

    private void OnTimelineDragUpdate(GestureDrag sender, GestureDrag.DragUpdateSignalArgs args)
    {
        if (!_isScrubbing) return;
        _registerActivity();
        sender.GetStartPoint(out var startX, out _);
        UpdateScrubCue(startX + args.OffsetX);
    }

    private void OnTimelineDragEnd(GestureDrag sender, GestureDrag.DragEndSignalArgs args)
    {
        if (!_isScrubbing) return;
        _isScrubbing = false;
        _positionLabel.RemoveCssClass("player-time-scrubbing");
        _timeline.RemoveCssClass("dragging");
        _scrubCue.SetVisible(false);
        CancelThrottledSeek();

        var finalPosition = _timeline.GetValue();
        SeekAbsolute(finalPosition);
        _timelinePlaybackPosition = TimeSpan.FromSeconds(finalPosition);
        _positionLabel.SetText(FormatTime(_timelinePlaybackPosition));
        _registerActivity();
    }

    private void OnTimelineValueChanged(object? sender, EventArgs args)
    {
        if (_updatingControls || !_timeline.GetSensitive()) return;

        var targetSeconds = _timeline.GetValue();
        _timelinePlaybackPosition = TimeSpan.FromSeconds(targetSeconds);
        _positionLabel.SetText(FormatTime(_timelinePlaybackPosition));

        if (_isScrubbing)
        {
            _latestScrubPosition = targetSeconds;
            var now = Environment.TickCount64;
            var elapsed = now - _lastThrottledSeekTime;
            if (elapsed >= SeekThrottleIntervalMilliseconds && _throttledSeekSource == 0)
            {
                _lastThrottledSeekTime = now;
                SeekAbsolute(_latestScrubPosition, false);
            }
            else if (_throttledSeekSource == 0)
            {
                var delay = Math.Max(10u, (uint)(SeekThrottleIntervalMilliseconds - elapsed));
                _throttledSeekSource = Functions.TimeoutAdd(0, delay, () =>
                {
                    _throttledSeekSource = 0;
                    if (_disposed || !_isScrubbing) return false;
                    _lastThrottledSeekTime = Environment.TickCount64;
                    SeekAbsolute(_latestScrubPosition, false);
                    return false;
                });
            }
        }
        else
        {
            SeekAbsolute(targetSeconds);
            _registerActivity();
        }
    }

    private void CancelThrottledSeek()
    {
        if (_throttledSeekSource == 0) return;
        Functions.SourceRemove(_throttledSeekSource);
        _throttledSeekSource = 0;
    }

    private LibMpvChapter? FindChapterAt(TimeSpan position)
    {
        LibMpvChapter? match = null;
        foreach (var chapter in _chapters)
        {
            if (chapter.Start <= position)
                match = chapter;
            else
                break;
        }

        return match;
    }

    private static string FormatDelta(TimeSpan delta)
    {
        var sign = delta < TimeSpan.Zero ? "-" : "+";
        var abs = delta.Duration();
        return abs.TotalHours >= 1
            ? $"{sign}{(int)abs.TotalHours}:{abs.Minutes:D2}:{abs.Seconds:D2}"
            : $"{sign}{(int)abs.TotalMinutes}:{abs.Seconds:D2}";
    }

    private static string FormatTime(TimeSpan value)
    {
        var seconds = Math.Max(0, (long)Math.Floor(value.TotalSeconds));
        var duration = TimeSpan.FromSeconds(seconds);
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{duration.Minutes}:{duration.Seconds:D2}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelThrottledSeek();
        _timelineMotionController.OnMotion -= OnTimelineMotion;
        _timelineMotionController.OnLeave -= OnTimelineLeave;
        _timelineOverlay.RemoveController(_timelineMotionController);
        _timelineMotionController.Dispose();

        _timelineDragGesture.OnDragBegin -= OnTimelineDragBegin;
        _timelineDragGesture.OnDragUpdate -= OnTimelineDragUpdate;
        _timelineDragGesture.OnDragEnd -= OnTimelineDragEnd;
        _timeline.RemoveController(_timelineDragGesture);
        _timelineDragGesture.Dispose();

        _timeline.OnValueChanged -= OnTimelineValueChanged;
    }
}
