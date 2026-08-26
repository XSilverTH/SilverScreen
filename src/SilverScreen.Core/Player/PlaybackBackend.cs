namespace SilverScreen.Core.Player;

public static class PlaybackBackends
{
    public const string ExternalMpv = "External MPV";
    public const string EmbeddedPlayer = "Embedded Player";

    public static bool IsEmbedded(string? backend)
    {
        return string.Equals(backend, EmbeddedPlayer, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(backend, "Internal player", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(backend, "embedded-player", StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string? backend)
    {
        return IsEmbedded(backend) ? EmbeddedPlayer : ExternalMpv;
    }
}