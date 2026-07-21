namespace SilverScreen.Core.Services;

/// <summary>Provides user-facing recovery guidance for runtime dependencies.</summary>
public static class RuntimeDependencyGuidance
{
    public const string SecretServiceUnavailable =
        "Secret Service is unavailable. Install libsecret and unlock or start a Secret Service provider, such as GNOME Keyring or KWallet, then retry.";

    public static string YtDlpUnavailable(string executablePath)
    {
        return $"yt-dlp could not be started from '{FormatExecutablePath(executablePath)}'. Install yt-dlp, then set its executable path in Preferences → Search (yt-dlp).";
    }

    public static string YtDlpFailed(string detail)
    {
        return $"yt-dlp failed: {detail} Update yt-dlp, verify your network connection, and retry.";
    }

    public const string YtDlpTimedOut =
        "yt-dlp timed out. Verify your network connection, update yt-dlp, and retry.";

    public static string MpvUnavailable(string executablePath)
    {
        return $"MPV could not be started from '{FormatExecutablePath(executablePath)}'. Install MPV, then set its executable path in Preferences → MPV Configuration.";
    }

    private static string FormatExecutablePath(string executablePath)
    {
        return string.IsNullOrWhiteSpace(executablePath) ? "the empty path" : executablePath.Trim();
    }
}
