namespace SilverScreen.Core.Player;

public static class PlaybackResult
{
    public static bool IsSuccessStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return false;

        var s = status.Trim();
        return s.StartsWith("Opening in MPV", StringComparison.OrdinalIgnoreCase) ||
               s.StartsWith("Opening embedded player", StringComparison.OrdinalIgnoreCase) ||
               s.StartsWith("Playback started", StringComparison.OrdinalIgnoreCase) ||
               s.StartsWith("Embedded presenter called", StringComparison.OrdinalIgnoreCase) ||
               s.StartsWith("External playback called", StringComparison.OrdinalIgnoreCase);
    }
}