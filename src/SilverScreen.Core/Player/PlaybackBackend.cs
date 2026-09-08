using SilverScreen.Core.Preferences;

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

    public static bool IsEmbedded(PlaybackBackendKind kind)
    {
        return kind == PlaybackBackendKind.Embedded;
    }

    public static string ToPersistedString(this PlaybackBackendKind kind)
    {
        return kind == PlaybackBackendKind.Embedded ? EmbeddedPlayer : ExternalMpv;
    }

    public static string ToDisplayName(this PlaybackBackendKind kind)
    {
        return kind == PlaybackBackendKind.Embedded ? EmbeddedDisplayName : ExternalMpvDisplayName;
    }

    public static string ToShortName(this PlaybackBackendKind kind)
    {
        return kind == PlaybackBackendKind.Embedded ? EmbeddedShortName : ExternalMpvShortName;
    }

    /// <summary>
    ///     Single legacy-string parser. Accepts persisted values, chooser labels, and historical
    ///     aliases (case-insensitive); anything unrecognized maps to
    ///     <see cref="PlaybackBackendKind.ExternalMpv" />, matching the old Normalize behavior.
    /// </summary>
    public static PlaybackBackendKind Parse(string? backend)
    {
        if (string.IsNullOrWhiteSpace(backend))
            return PlaybackBackendKind.ExternalMpv;

        var value = backend.Trim();
        return value.Equals(EmbeddedPlayer, StringComparison.OrdinalIgnoreCase) ||
               value.Equals(EmbeddedDisplayName, StringComparison.OrdinalIgnoreCase) ||
               value.Equals(EmbeddedShortName, StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Internal player", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Embedded", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("LibMpv", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("embedded-player", StringComparison.OrdinalIgnoreCase)
            ? PlaybackBackendKind.Embedded
            : PlaybackBackendKind.ExternalMpv;
    }
}

