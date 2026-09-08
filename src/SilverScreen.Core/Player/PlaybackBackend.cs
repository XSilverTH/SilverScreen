namespace SilverScreen.Core.Player;

public static class PlaybackBackends
{
    // Persisted values. Stored in preferences JSON — NEVER change these strings.
    public const string ExternalMpv = "External MPV";
    public const string EmbeddedPlayer = "Embedded Player";

    // Canonical chooser labels for the Preferences dialog. Display-only; never persisted.
    public const string ExternalMpvDisplayName = "External MPV (separate window)";
    private const string EmbeddedDisplayName = "Built-in player";

    // Short labels for tooltips, logs, and other compact surfaces.
    public const string ExternalMpvShortName = "External MPV";
    private const string EmbeddedShortName = "Built-in";

    public static bool IsEmbedded(string? backend)
    {
        return string.Equals(backend, EmbeddedPlayer, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(backend, EmbeddedDisplayName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(backend, EmbeddedShortName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(backend, "Internal player", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(backend, "Embedded", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(backend, "LibMpv", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(backend, "embedded-player", StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string? backend)
    {
        return IsEmbedded(backend) ? EmbeddedPlayer : ExternalMpv;
    }
}