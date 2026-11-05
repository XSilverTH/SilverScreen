using SilverScreen.Core.Common;

namespace SilverScreen.Player.Controllers;

public sealed record PlayerErrorInfo(
    string Title,
    string Message,
    string IconName,
    string TechnicalDetail);

public static class PlayerErrorClassifier
{
    public static PlayerErrorInfo Classify(string? rawDetail)
    {
        var sanitized = DiagnosticSanitizer.Sanitize(rawDetail?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(sanitized))
            return new PlayerErrorInfo(
                "Playback Failed",
                "An unexpected playback error occurred.",
                "dialog-error-symbolic",
                "No additional error detail was reported.");

        var lower = sanitized.ToLowerInvariant();

        // 1. Account / Sign-in required / Bot verification
        if (lower.Contains("bot") || lower.Contains("sign in") || lower.Contains("login") ||
            lower.Contains("age-restricted"))
            return new PlayerErrorInfo(
                "Sign-in Required",
                "YouTube requires account verification or sign-in to view this video.",
                "avatar-default-symbolic",
                sanitized);

        // 2. Network connectivity / DNS / Timeout / Connection refused
        if (lower.Contains("network") || lower.Contains("connection") || lower.Contains("timed out") ||
            lower.Contains("timeout") || lower.Contains("resolve host") || lower.Contains("unreachable") ||
            lower.Contains("refused") || lower.Contains("handshake") || lower.Contains("name or service not known"))
            return new PlayerErrorInfo(
                "Connection Failed",
                "Could not reach YouTube servers. Check your internet connection and try again.",
                "network-error-symbolic",
                sanitized);

        // 3. Unavailable / Private / Geo-restricted / Removed
        if (lower.Contains("unavailable") || lower.Contains("private") || lower.Contains("removed") ||
            lower.Contains("not available in your country") || lower.Contains("geo") || lower.Contains("blocked") ||
            lower.Contains("premieres in") || lower.Contains("this video has been removed"))
            return new PlayerErrorInfo(
                "Video Unavailable",
                "This video is private, removed, or unavailable in your location.",
                "dialog-error-symbolic",
                sanitized);

        // 4. Stream / Format extraction
        if (lower.Contains("ytdl") || lower.Contains("yt-dlp") || lower.Contains("format") ||
            lower.Contains("extractor"))
            return new PlayerErrorInfo(
                "Stream Extraction Failed",
                "The media extractor could not resolve a compatible video stream.",
                "dialog-error-symbolic",
                sanitized);

        // 5. OpenGL / Graphics context
        if (lower.Contains("opengl") || lower.Contains("context") || lower.Contains("glarea"))
            return new PlayerErrorInfo(
                "Graphics Context Error",
                "Could not create an OpenGL context for embedded video rendering.",
                "dialog-error-symbolic",
                sanitized);

        // 6. Generic playback failure
        return new PlayerErrorInfo(
            "Playback Failed",
            "The media player encountered an error while loading or decoding this video.",
            "dialog-error-symbolic",
            sanitized);
    }
}