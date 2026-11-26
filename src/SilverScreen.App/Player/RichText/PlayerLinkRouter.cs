using System.Diagnostics;
using System.Globalization;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Player;

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
    private static readonly ILogger Logger = Log.ForContext<PlayerLinkRouter>();

    public PlayerLinkRouter(
        INavigationService navigation,
        IPlaybackService playback,
        Action<double> seekRequested,
        Action<string>? externalRequested = null)
        : this(
            seekRequested,
            video => playback.PlayAsync(new PlaybackRequest([video])).FireAndForget(Logger),
            (url, name) => navigation.NavigateToChannel(url, name),
            tag => navigation.NavigateToSearch("#" + tag),
            externalRequested ?? (_ => { }))
    {
    }

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

        if (!IsWebUrl(uri)) return true;
        var parsed = YouTubeUrlParser.Parse(uri);
        return parsed switch
        {
            YouTubeUrlParseResult.Video(var videoId) => ActivateVideo(videoId, parsed.CanonicalWatchUrl),
            YouTubeUrlParseResult.Shorts(var videoId) => ActivateVideo(videoId, parsed.CanonicalWatchUrl),
            YouTubeUrlParseResult.Channel(var channelPath) => ActivateChannel(channelPath),
            YouTubeUrlParseResult.NotYouTube or YouTubeUrlParseResult.Playlist
                or YouTubeUrlParseResult.UnknownYouTube or YouTubeUrlParseResult.Invalid => ActivateExternal(uri),
            _ => throw new UnreachableException()
        };
    }

    private bool ActivateVideo(string videoId, string? canonicalWatchUrl)
    {
        videoRequested(new VideoSummary(
            videoId,
            $"YouTube video {videoId}",
            "YouTube",
            TimeSpan.Zero,
            string.Empty,
            false,
            canonicalWatchUrl));
        return true;
    }

    private bool ActivateChannel(string channelPath)
    {
        var normalizedChannel = channelPath.Trim('/');
        channelRequested($"https://www.youtube.com/{normalizedChannel}", normalizedChannel);
        return true;
    }

    private bool ActivateExternal(string uri)
    {
        externalRequested(uri);
        return true;
    }

    private static bool TryParseSeekUri(string uri, out double seconds)
    {
        seconds = 0;
        return uri.StartsWith(RichLabelMarkup.SeekScheme, StringComparison.Ordinal) &&
               double.TryParse(uri.AsSpan(RichLabelMarkup.SeekScheme.Length),
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out seconds) &&
               seconds >= 0;
    }

    private static bool TryParseHashtagUri(string uri, out string tag)
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