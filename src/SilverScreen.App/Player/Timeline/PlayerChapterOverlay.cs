using GObject;
using Gtk;
using SilverScreen.Infrastructure.Player;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Player.Timeline;

internal sealed class PlayerChapterOverlay(
    Overlay host,
    Scale timeline,
    Func<TimeSpan> playbackPosition,
    Action<double> seekAbsolute,
    Action registerActivity)
    : IDisposable
{
    private readonly DisposeScope _lifetime = new();
    private readonly List<(Button Marker, SignalHandler<Button> Handler)> _markers = [];
    private IReadOnlyList<LibMpvChapter> _chapters = [];
    private TimeSpan _duration;
    private int _hostWidth = -1;
    private bool _layoutDirty;
    private int _trackStart = -1;
    private int _trackWidth = -1;

    public void Dispose()
    {
        if (_lifetime.IsDisposed) return;
        _lifetime.Dispose();
        ClearMarkers();
    }

    public void Update(IReadOnlyList<LibMpvChapter> chapters, TimeSpan duration, bool layoutNow = true)
    {
        if (_lifetime.IsDisposed) return;
        if (!AreChaptersEqual(_chapters, chapters))
        {
            ClearMarkers();
            _chapters = chapters;
            _trackStart = -1;
            _trackWidth = -1;
            _hostWidth = -1;
            _layoutDirty = true;
            foreach (var chapter in chapters)
            {
                var marker = Button.New();
                marker.AddCssClass("player-chapter-marker");
                marker.SetChild(CreateMarkerLine());
                marker.SetTooltipText(chapter.Title);
                var startSeconds = chapter.Start.TotalSeconds;
                SignalHandler<Button> handler = (_, _) =>
                {
                    seekAbsolute(startSeconds);
                    registerActivity();
                };
                marker.OnClicked += handler;
                marker.Halign = Align.Start;
                marker.Valign = Align.Center;
                host.AddOverlay(marker);
                _markers.Add((marker, handler));
            }
        }

        if (_markers.Count == 0)
        {
            _duration = duration;
            _layoutDirty = false;
            return;
        }

        if (!layoutNow)
        {
            if (_duration != duration)
                _layoutDirty = true;
            _duration = duration;
            return;
        }

        Layout(duration);
    }

    internal void Layout()
    {
        if (_markers.Count == 0) return;
        Layout(_duration);
    }

    private void Layout(TimeSpan duration)
    {
        if (_markers.Count == 0)
        {
            _duration = duration;
            _layoutDirty = false;
            return;
        }

        var hostWidth = host.GetAllocatedWidth();
        if (!_layoutDirty && _duration == duration && _hostWidth == hostWidth && _trackWidth > 0)
            return;

        var (trackStart, trackWidth) = PlayerTimelineGeometry.GetTrack(
            timeline,
            host,
            playbackPosition(),
            duration);

        if (!_layoutDirty && _duration == duration && _hostWidth == hostWidth &&
            _trackStart == trackStart && _trackWidth == trackWidth)
            return;

        _layoutDirty = false;
        _duration = duration;
        _hostWidth = hostWidth;
        _trackStart = trackStart;
        _trackWidth = trackWidth;
        var hasDuration = duration > TimeSpan.Zero && trackWidth > 0 && hostWidth > 0;
        for (var index = 0; index < _markers.Count; index++)
        {
            var marker = _markers[index].Marker;
            var chapter = _chapters[index];
            marker.SetVisible(hasDuration && chapter.Start <= duration);
            if (!hasDuration) continue;
            var markerX = PlayerTimelineState.CalculateChapterMarkerPosition(
                chapter.Start,
                duration,
                trackStart,
                trackWidth,
                hostWidth);
            marker.MarginStart = (int)markerX;
        }
    }

    private void ClearMarkers()
    {
        foreach (var (marker, handler) in _markers)
        {
            marker.OnClicked -= handler;
            host.RemoveOverlay(marker);
            marker.Dispose();
        }

        _markers.Clear();
    }

    private static bool AreChaptersEqual(IReadOnlyList<LibMpvChapter> a, IReadOnlyList<LibMpvChapter> b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (a[i] != b[i])
                return false;
        return true;
    }

    private static Box CreateMarkerLine()
    {
        var line = Box.New(Orientation.Vertical, 0);
        line.AddCssClass("player-chapter-marker-line");
        line.Halign = Align.Center;
        line.Valign = Align.Center;
        return line;
    }
}