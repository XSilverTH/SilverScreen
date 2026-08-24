using Gtk;
using SilverScreen.Infrastructure.Player;

namespace SilverScreen.Player.Controllers;

internal sealed class PlayerChapterOverlay(
    Overlay host,
    Scale timeline,
    Func<TimeSpan> playbackPosition,
    Action<double> seekAbsolute,
    Action registerActivity)
    : IDisposable
{
    private readonly List<Button> _markers = [];
    private IReadOnlyList<LibMpvChapter> _chapters = [];
    private bool _disposed;
    private TimeSpan _duration;
    private int _trackStart = -1;
    private int _trackWidth = -1;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ClearMarkers();
    }

    public void Update(IReadOnlyList<LibMpvChapter> chapters, TimeSpan duration)
    {
        if (_disposed) return;
        if (!_chapters.SequenceEqual(chapters))
        {
            ClearMarkers();
            _chapters = chapters;
            _trackStart = -1;
            _trackWidth = -1;
            foreach (var chapter in chapters)
            {
                var marker = Button.New();
                marker.AddCssClass("player-chapter-marker");
                marker.SetChild(CreateMarkerLine());
                marker.SetTooltipText(chapter.Title);
                marker.OnClicked += (_, _) =>
                {
                    seekAbsolute(chapter.Start.TotalSeconds);
                    registerActivity();
                };
                marker.Halign = Align.Start;
                marker.Valign = Align.Center;
                host.AddOverlay(marker);
                _markers.Add(marker);
            }
        }

        Layout(duration);
    }

    internal void Layout()
    {
        Layout(_duration);
    }

    private void Layout(TimeSpan duration)
    {
        var (trackStart, trackWidth) = PlayerTimelineGeometry.GetTrack(
            timeline,
            host,
            playbackPosition(),
            duration);

        if (_duration == duration && _trackStart == trackStart && _trackWidth == trackWidth) return;
        _duration = duration;
        _trackStart = trackStart;
        _trackWidth = trackWidth;
        var hostWidth = host.GetAllocatedWidth();
        var hasDuration = duration > TimeSpan.Zero && trackWidth > 0 && hostWidth > 0;
        for (var index = 0; index < _markers.Count; index++)
        {
            var marker = _markers[index];
            var chapter = _chapters[index];
            marker.SetVisible(hasDuration && chapter.Start <= duration);
            if (!hasDuration) continue;
            var markerX = PlayerTimelineEngine.CalculateChapterMarkerPosition(
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
        foreach (var marker in _markers)
        {
            host.RemoveOverlay(marker);
            marker.Dispose();
        }

        _markers.Clear();
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