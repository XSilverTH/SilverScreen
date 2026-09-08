using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Common;

namespace SilverScreen.Player.RichText;

/// <summary>
///     Routes activated rich-text links: timestamps seek within the current video,
///     hashtags search in-app, supported YouTube links navigate in-app, and
///     everything else opens in the system browser.
/// </summary>
internal sealed class PlayerLinkRouter(
    Action<double> seekRequested,
    Action<VideoSummary> videoRequested,
    Action<string, string> channelRequested,
    Action<string> searchRequested,
    Action<string> externalRequested)
{
    public bool Activate(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return true;

        if (TryParseSeekUri(uri, out var seconds))
        {
            seekRequested(seconds);
            return true;
        }

        if (TryParseHashtagUri(uri, out var tag))
        {
            searchRequested(tag);
            return true;
        }

        if (IsWebUrl(uri))
        {
            var parsed = YouTubeUrlParser.Parse(uri);
            switch (parsed)
            {
                case { Kind: YouTubeUrlKind.Video or YouTubeUrlKind.Shorts, VideoId: not null }:
                    videoRequested(new VideoSummary(
                        parsed.VideoId,
                        $"YouTube video {parsed.VideoId}",
                        "YouTube",
                        TimeSpan.Zero,
                        string.Empty,
                        false,
                        parsed.CanonicalWatchUrl));
                    return true;
                case { Kind: YouTubeUrlKind.Channel, ChannelPath: not null }:
                    channelRequested(
                        $"https://www.youtube.com/{parsed.ChannelPath.Trim('/')}",
                        parsed.ChannelPath.Trim('/'));
                    return true;
                default:
                    externalRequested(uri);
                    return true;
            }
        }

        return true;
    }

    public static bool TryParseSeekUri(string uri, out double seconds)
    {
        seconds = 0;
        return uri.StartsWith(RichLabelMarkup.SeekScheme, StringComparison.Ordinal) &&
               double.TryParse(uri.AsSpan(RichLabelMarkup.SeekScheme.Length),
                   System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out seconds) &&
               seconds >= 0;
    }

    public static bool TryParseHashtagUri(string uri, out string tag)
    {
        tag = string.Empty;
        if (!uri.StartsWith(RichLabelMarkup.HashtagScheme, StringComparison.Ordinal))
            return false;

        tag = uri[RichLabelMarkup.HashtagScheme.Length..];
        return tag.Length > 0;
    }

    private static bool IsWebUrl(string uri)
    {
        return Uri.TryCreate(uri, UriKind.Absolute, out var absolute) &&
               (absolute.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                absolute.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }
}
