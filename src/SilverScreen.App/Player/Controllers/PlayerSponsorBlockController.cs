using Cairo;
using Gtk;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using static GLib.Functions;

namespace SilverScreen.Player.Controllers;

/// <summary>
///     Lightweight presentation controller that binds SponsorBlock segment rendering
///     and skip prompt UI to the underlying <see cref="PlaybackSession" /> state and events.
/// </summary>
internal sealed class PlayerSponsorBlockController : IDisposable
{
    private const uint SkipPromptDurationMilliseconds = PlayerTimelineEngine.DefaultSkipPromptDurationMilliseconds;
    private readonly PlaybackSession _session;
    private readonly IPreferencesService _preferences;
    private readonly Button _skipButton;
    private readonly Label _skipLabel;
    private readonly Revealer _skipRevealer;
    private readonly Scale _timeline;
    private readonly DrawingArea _timelineDrawingArea;
    private readonly Overlay _timelineOverlay;
    private bool _disposed;
    private uint _promptHideSource;
    private string? _skipButtonColorClass;

    public PlayerSponsorBlockController(
        PlaybackSession session,
        IPreferencesService preferences,
        Scale timeline,
        Overlay timelineOverlay,
        Revealer skipRevealer,
        Button skipButton,
        Label skipLabel)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _timeline = timeline;
        _timelineOverlay = timelineOverlay;
        _skipRevealer = skipRevealer;
        _skipButton = skipButton;
        _skipLabel = skipLabel;

        _timelineDrawingArea = DrawingArea.New();
        _timelineDrawingArea.SetCanTarget(false);
        _timelineDrawingArea.Halign = Align.Fill;
        _timelineDrawingArea.Valign = Align.Fill;
        _timelineDrawingArea.SetDrawFunc(DrawTimeline);
        _timelineOverlay.AddOverlay(_timelineDrawingArea);

        _session.SponsorBlockSegmentsChanged += OnSegmentsChanged;
        _session.SponsorBlockPromptChanged += OnPromptChanged;
        _session.SessionEnded += OnSessionEnded;
        _session.Failed += OnSessionFailed;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.SponsorBlockSegmentsChanged -= OnSegmentsChanged;
        _session.SponsorBlockPromptChanged -= OnPromptChanged;
        _session.SessionEnded -= OnSessionEnded;
        _session.Failed -= OnSessionFailed;
        HideManualPrompt();
        _timelineOverlay.RemoveOverlay(_timelineDrawingArea);
        _timelineDrawingArea.Dispose();
    }

    public bool TrySkipManualSegment()
    {
        if (_disposed) return false;
        var handled = _session.TrySkipManualSegment();
        if (handled) HideManualPrompt();
        return handled;
    }

    public void Redraw()
    {
        if (!_disposed)
            _timelineDrawingArea.QueueDraw();
    }

    private void OnSegmentsChanged(IReadOnlyList<SponsorBlockSegment> segments)
    {
        IdleAdd(0, () =>
        {
            if (_disposed) return false;
            _timelineDrawingArea.QueueDraw();
            return false;
        });
    }

    private void OnPromptChanged(SponsorBlockSegment? segment)
    {
        IdleAdd(0, () =>
        {
            if (_disposed) return false;
            if (segment is not null)
                ShowManualPrompt(segment);
            else
                HideManualPrompt();
            return false;
        });
    }

    private void OnSessionEnded()
    {
        IdleAdd(0, () =>
        {
            if (_disposed) return false;
            HideManualPrompt();
            _timelineDrawingArea.QueueDraw();
            return false;
        });
    }

    private void OnSessionFailed(string detail)
    {
        IdleAdd(0, () =>
        {
            if (_disposed) return false;
            HideManualPrompt();
            _timelineDrawingArea.QueueDraw();
            return false;
        });
    }

    private void DrawTimeline(DrawingArea drawingArea, Context context, int width, int height)
    {
        var preferences = _preferences.GetPreferences();
        if (!preferences.SponsorBlockSegmentDisplayEnabled || width <= 0 || height <= 0)
            return;

        var duration = _session.LastPlaybackState?.Duration > TimeSpan.Zero
            ? _session.LastPlaybackState.Duration
            : _session.CurrentVideo?.Duration ?? TimeSpan.Zero;

        if (duration <= TimeSpan.Zero) return;

        var position = _session.LastPlaybackState?.Position ?? TimeSpan.Zero;
        var (trackStart, trackWidth) = PlayerTimelineGeometry.GetTrack(_timeline, drawingArea, position, duration);
        if (trackWidth <= 0) return;

        const double rangeHeight = 10;
        var rangeY = Math.Max(0, (height - rangeHeight) / 2);

        foreach (var segment in _session.SponsorBlockSegments)
        {
            if (segment.Start >= duration) continue;
            var start = PlayerTimelineGeometry.GetTrackPosition(segment.Start, duration, trackStart, trackWidth);
            var end = PlayerTimelineGeometry.GetTrackPosition(segment.End, duration, trackStart, trackWidth);
            var color = SponsorBlockCategories.GetColor(segment.Category);
            context.SetSourceRgba(color.Red / 255.0, color.Green / 255.0, color.Blue / 255.0, color.Opacity);
            context.Rectangle(start, rangeY, Math.Max(2, end - start), rangeHeight);
            context.Fill();
        }
    }

    private void ShowManualPrompt(SponsorBlockSegment segment)
    {
        var category = PlayerTimelineEngine.GetSponsorBlockCategoryLabel(segment.Category);
        _skipLabel.SetText($"Skip {category}");
        _skipButton.SetTooltipText($"Skip {category} (Enter)");
        SetSkipButtonColor(segment.Category);
        _skipRevealer.RevealChild = true;
        if (_promptHideSource != 0) SourceRemove(_promptHideSource);
        _promptHideSource = TimeoutAdd(0, SkipPromptDurationMilliseconds, () =>
        {
            _promptHideSource = 0;
            if (!_disposed)
            {
                _skipRevealer.RevealChild = false;
                _session.DismissSponsorBlockPrompt();
            }
            return false;
        });
    }

    private void HideManualPrompt()
    {
        if (_promptHideSource != 0)
        {
            SourceRemove(_promptHideSource);
            _promptHideSource = 0;
        }

        if (_disposed) return;
        _skipRevealer.RevealChild = false;
        ClearSkipButtonColor();
    }

    private void SetSkipButtonColor(string category)
    {
        ClearSkipButtonColor();
        var colorClass = PlayerTimelineEngine.GetSponsorBlockButtonColorClass(category);
        _skipButton.AddCssClass(colorClass);
        _skipButtonColorClass = colorClass;
    }

    private void ClearSkipButtonColor()
    {
        if (_skipButtonColorClass is null) return;
        _skipButton.RemoveCssClass(_skipButtonColorClass);
        _skipButtonColorClass = null;
    }
}
