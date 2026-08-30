using System.Text.RegularExpressions;
using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Core.Player;

public sealed record ResolvedMediaStream(
    string Url,
    string? FormatId,
    int? Height,
    int? Width,
    int? Fps,
    string? VideoCodec,
    string? AudioCodec,
    double? Bitrate,
    bool HasVideo,
    bool HasAudio);

public sealed record ResolvedMedia(
    string VideoUrl,
    string? AudioUrl,
    string Quality,
    DateTimeOffset? ExpiresAt,
    YouTubeVideoDetails? Details);

public sealed record YouTubeMediaResolutionResult(
    ResolvedMedia? Media,
    YouTubeVideoDetails? Details,
    bool IsSuccess,
    string StatusMessage)
{
    public static YouTubeMediaResolutionResult Success(ResolvedMedia media)
    {
        return new YouTubeMediaResolutionResult(media, media.Details, true, "Media resolved successfully.");
    }

    public static YouTubeMediaResolutionResult Failure(string message)
    {
        return new YouTubeMediaResolutionResult(null, null, false, message);
    }
}

public static partial class YouTubeMediaExpiryParser
{
    [GeneratedRegex(@"[?&]expire=(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ExpireRegex();

    public static DateTimeOffset? TryExtractExpiry(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var match = ExpireRegex().Match(url);
        if (!match.Success || !long.TryParse(match.Groups[1].ValueSpan, out var unixSeconds)) return null;
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

    }
}