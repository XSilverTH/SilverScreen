using Gtk;
using SilverScreen.Infrastructure.Player;
using Functions = GLib.Functions;

namespace SilverScreen.Player.Controllers;

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
        Engine = engine ?? new PlayerTimelineEngine();

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

    public bool IsScrubbing => Engine.IsScrubbing;

    public TimeSpan PlaybackPosition => Engine.PlaybackPosition;

    private PlayerTimelineEngine Engine { get; }

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

    public void UpdatePosition(LibMpvPlaybackState state)
    {
        if (_disposed) return;

        _updatingControls = true;
        try
        {
            _durationLabel.SetText(PlayerTimelineEngine.FormatDurationLabel(state.Duration));
            _timeline.SetRange(0, Math.Max(0, state.Duration.TotalSeconds));
            _timeline.SetSensitive(state.IsSeekable && state.Duration > TimeSpan.Zero);

            if (!Engine.UpdatePlaybackState(state.HasMedia, state.Position, state.Duration, state.Chapters,
                    out var accepted) || !accepted) return;
            _positionLabel.SetText(PlayerTimelineEngine.FormatTime(state.Position));
            _timeline.SetValue(Math.Clamp(state.Position.TotalSeconds, 0, Math.Max(0, state.Duration.TotalSeconds)));
        }
        finally
        {
            _updatingControls = false;
        }
    }

    public void SeekAbsolute(double position, bool exact = true)
    {
        Engine.RegisterSeek(position);
        _seekAbsolute(position, exact);
    }

    public void CancelScrubbing()
    {
        if (!IsScrubbing) return;
        var restoredPosition = Engine.CancelScrub();
        CancelThrottledSeek();
        _positionLabel.RemoveCssClass("player-time-scrubbing");
        _timeline.RemoveCssClass("dragging");
        _scrubCue.SetVisible(false);
        _updatingControls = true;
        try
        {
            _timeline.SetValue(restoredPosition.TotalSeconds);
            _positionLabel.SetText(PlayerTimelineEngine.FormatTime(restoredPosition));
        }
        finally
        {
            _updatingControls = false;
        }
    }

    public void Reset()
    {
        CancelScrubbing();
        Engine.Reset();
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
        Engine.SetDuration(duration);
        _durationLabel.SetText(PlayerTimelineEngine.FormatDurationLabel(duration));
    }

    private void OnTimelineMotion(EventControllerMotion sender, EventControllerMotion.MotionSignalArgs args)
    {
        if (_disposed || !Engine.HasMedia || Engine.Duration <= TimeSpan.Zero || !_timeline.GetSensitive())
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
        var currentPosition = IsScrubbing ? TimeSpan.FromSeconds(_timeline.GetValue()) : Engine.PlaybackPosition;
        var (trackStart, trackWidth) = PlayerTimelineGeometry.GetTrack(
            _timeline,
            _timelineOverlay,
            currentPosition,
            Engine.Duration);

        var targetTime =
            PlayerTimelineGeometry.GetPositionAtCoordinate(pointerX, trackStart, trackWidth, Engine.Duration);

        var cueWidth = _scrubCue.GetAllocatedWidth();
        var hostWidth = _timelineOverlay.GetAllocatedWidth();
        var badgeX = PlayerTimelineEngine.CalculateScrubCueBadgePosition(pointerX, cueWidth, hostWidth);
        _scrubCue.MarginStart = (int)Math.Round(badgeX);

        _scrubTimeLabel.SetText(PlayerTimelineEngine.FormatTime(targetTime));

        if (IsScrubbing)
        {
            var delta = Engine.CalculateScrubDelta(targetTime);
            _scrubDeltaLabel.SetText(PlayerTimelineEngine.FormatDelta(delta));
            _scrubDeltaLabel.SetVisible(true);
        }
        else
        {
            _scrubDeltaLabel.SetVisible(false);
        }

        var chapter = Engine.GetChapterAt(targetTime);
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
        if (_disposed || !Engine.HasMedia || !_timeline.GetSensitive() || Engine.Duration <= TimeSpan.Zero)
            return;

        Engine.BeginScrub(_timeline.GetValue());
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
        Engine.EndScrub(finalPosition);
        _seekAbsolute(finalPosition, true);
        _positionLabel.SetText(PlayerTimelineEngine.FormatTime(Engine.PlaybackPosition));
        _registerActivity();
    }

    private void OnTimelineValueChanged(object? sender, EventArgs args)
    {
        if (_updatingControls || !_timeline.GetSensitive()) return;

        var targetSeconds = _timeline.GetValue();
        Engine.SetPositionDirect(TimeSpan.FromSeconds(targetSeconds));
        _positionLabel.SetText(PlayerTimelineEngine.FormatTime(Engine.PlaybackPosition));

        if (IsScrubbing)
        {
            Engine.UpdateScrub(targetSeconds);
            if (Engine.ShouldDispatchThrottledSeek(out var delay) && _throttledSeekSource == 0)
                SeekAbsolute(Engine.LatestScrubPositionSeconds, false);
            else if (_throttledSeekSource == 0)
                _throttledSeekSource = Functions.TimeoutAdd(0, delay, () =>
                {
                    _throttledSeekSource = 0;
                    if (_disposed || !IsScrubbing) return false;
                    Engine.RecordThrottledSeekDispatched();
                    SeekAbsolute(Engine.LatestScrubPositionSeconds, false);
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
        if (_throttledSeekSource == 0) return;
        Functions.SourceRemove(_throttledSeekSource);
        _throttledSeekSource = 0;
    }
}