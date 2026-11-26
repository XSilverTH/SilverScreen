using System.Text.RegularExpressions;
using SilverScreen.Core.Domain;

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
    bool HasAudio,
    DateTimeOffset? ExpiresAt = null);

public sealed record ResolvedMedia(
    string VideoUrl,
    string? AudioUrl,
    string Quality,
    DateTimeOffset? ExpiresAt,
    YouTubeVideoDetails? Details);

public union YouTubeMediaResolutionResult(
    YouTubeMediaResolutionResult.Success,
    YouTubeMediaResolutionResult.Failure)
{
    public sealed record Success(ResolvedMedia Media)
    {
        public YouTubeVideoDetails? Details => Media.Details;
    }

    public sealed record Failure(string StatusMessage);
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