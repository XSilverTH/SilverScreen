using Graphene;
using Gtk;

namespace SilverScreen.Player.Controllers;

internal static class PlayerTimelineGeometry
{
    internal static (int Start, int Width) GetTrackBounds(int troughStart, int troughWidth, int sliderStart,
        int sliderEnd, TimeSpan playbackPosition, TimeSpan duration)
    {
        var sliderWidth = Math.Max(0, sliderEnd - sliderStart);
        var trackWidth = Math.Max(0, troughWidth - sliderWidth);
        if (duration <= TimeSpan.Zero || trackWidth == 0)
            return (troughStart + sliderWidth / 2, trackWidth);

        var sliderCenter = (sliderStart + sliderEnd) / 2d;
        var currentFraction = Math.Clamp(playbackPosition.TotalSeconds / duration.TotalSeconds, 0, 1);
        return ((int)Math.Round(sliderCenter - currentFraction * trackWidth), trackWidth);
    }

    internal static double GetTrackPosition(TimeSpan position, TimeSpan duration, int trackStart, int trackWidth)
    {
        if (duration <= TimeSpan.Zero || trackWidth <= 0) return trackStart;

        var fraction = Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1);
        return trackStart + fraction * trackWidth;
    }

    internal static TimeSpan GetPositionAtCoordinate(double coordinateX, int trackStart, int trackWidth,
        TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || trackWidth <= 0) return TimeSpan.Zero;

        var fraction = Math.Clamp((coordinateX - trackStart) / trackWidth, 0.0, 1.0);
        return TimeSpan.FromSeconds(fraction * duration.TotalSeconds);
    }

    internal static (int Start, int Width) GetTrack(Scale timeline, Widget coordinateTarget,
        TimeSpan playbackPosition, TimeSpan duration)
    {
        timeline.GetRangeRect(out var trough);
        timeline.GetSliderRange(out var sliderStart, out var sliderEnd);
        var (start, width) = GetTrackBounds(trough.X, trough.Width, sliderStart, sliderEnd,
            playbackPosition, duration);
        var startPoint = new Point { X = start, Y = 0 };
        var endPoint = new Point { X = start + width, Y = 0 };
        if (!timeline.ComputePoint(coordinateTarget, startPoint, out var transformedStart) ||
            !timeline.ComputePoint(coordinateTarget, endPoint, out var transformedEnd))
            return (start, width);

        return ((int)Math.Round(transformedStart.X), (int)Math.Round(transformedEnd.X - transformedStart.X));
    }
}