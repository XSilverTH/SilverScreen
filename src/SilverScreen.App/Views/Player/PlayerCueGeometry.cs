namespace SilverScreen.Views.Player;

internal static class PlayerCueGeometry
{
    public const double CueTriggerDistance = 80;
    public const double CueActiveDistance = 140;
    public const double CueEdgeMargin = 80;

    public static bool IsCommentsCueActive(double x, double y, double width, double height, bool isCurrentlyActive)
    {
        if (width <= 0 || height <= 0) return false;

        var edgeMargin = Math.Min(CueEdgeMargin, height / 4d);
        var threshold = isCurrentlyActive ? CueActiveDistance : CueTriggerDistance;

        return x >= 0 &&
               x <= threshold &&
               y >= edgeMargin &&
               y <= height - edgeMargin;
    }

    public static bool IsInfoCueActive(double x, double y, double width, double height, bool isCurrentlyActive)
    {
        if (width <= 0 || height <= 0) return false;

        var edgeMargin = Math.Min(CueEdgeMargin, width / 4d);
        var threshold = isCurrentlyActive ? CueActiveDistance : CueTriggerDistance;
        var distanceFromBottom = height - y;

        return distanceFromBottom >= 0 &&
               distanceFromBottom <= threshold &&
               x >= edgeMargin &&
               x <= width - edgeMargin;
    }
}
