namespace SilverScreen.Features.Search;

public enum YouTubeUrlKind
{
    NotYouTube,
    Video,
    Shorts,
    Channel,
    Playlist,
    UnknownYouTube,
    Invalid,
}

public sealed record YouTubeUrlParseResult(
    YouTubeUrlKind Kind,
    string? VideoId = null,
    string? ChannelPath = null,
    string? PlaylistId = null)
{
    public string? CanonicalWatchUrl => Kind == YouTubeUrlKind.Video && VideoId is not null
        ? $"https://www.youtube.com/watch?v={Uri.EscapeDataString(VideoId)}"
        : null;

    public static YouTubeUrlParseResult NotYouTube { get; } = new(YouTubeUrlKind.NotYouTube);

    public static YouTubeUrlParseResult UnknownYouTube { get; } = new(YouTubeUrlKind.UnknownYouTube);

    public static YouTubeUrlParseResult Invalid { get; } = new(YouTubeUrlKind.Invalid);
}

public static class YouTubeUrlParser
{
    private static readonly char[] PathSeparators = ['/'];

    public static YouTubeUrlParseResult Parse(string? input)
    {
        var text = input?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return YouTubeUrlParseResult.NotYouTube;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return YouTubeUrlParseResult.NotYouTube;
        }

        if (!IsSupportedScheme(uri))
        {
            return IsYouTubeHost(uri.Host) ? YouTubeUrlParseResult.Invalid : YouTubeUrlParseResult.NotYouTube;
        }

        if (!IsYouTubeHost(uri.Host))
        {
            return YouTubeUrlParseResult.NotYouTube;
        }

        return uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)
            ? ParseShortHost(uri)
            : ParseYouTubeHost(uri);
    }

    private static YouTubeUrlParseResult ParseShortHost(Uri uri)
    {
        var videoId = GetPathSegment(uri, 0);
        if (videoId is null)
        {
            return YouTubeUrlParseResult.UnknownYouTube;
        }

        return IsValidVideoId(videoId)
            ? new YouTubeUrlParseResult(YouTubeUrlKind.Video, VideoId: videoId)
            : YouTubeUrlParseResult.Invalid;
    }

    private static YouTubeUrlParseResult ParseYouTubeHost(Uri uri)
    {
        var firstSegment = GetPathSegment(uri, 0);
        if (firstSegment is null)
        {
            return YouTubeUrlParseResult.UnknownYouTube;
        }

        if (firstSegment.Equals("watch", StringComparison.OrdinalIgnoreCase))
        {
            var videoId = GetQueryValue(uri.Query, "v");
            if (string.IsNullOrWhiteSpace(videoId))
            {
                return YouTubeUrlParseResult.UnknownYouTube;
            }

            return IsValidVideoId(videoId)
                ? new YouTubeUrlParseResult(YouTubeUrlKind.Video, VideoId: videoId)
                : YouTubeUrlParseResult.Invalid;
        }

        if (firstSegment.Equals("shorts", StringComparison.OrdinalIgnoreCase))
        {
            var videoId = GetPathSegment(uri, 1);
            if (videoId is null)
            {
                return YouTubeUrlParseResult.UnknownYouTube;
            }

            return IsValidVideoId(videoId)
                ? new YouTubeUrlParseResult(YouTubeUrlKind.Shorts, VideoId: videoId)
                : YouTubeUrlParseResult.Invalid;
        }

        if (firstSegment.Equals("playlist", StringComparison.OrdinalIgnoreCase))
        {
            var playlistId = GetQueryValue(uri.Query, "list");
            return string.IsNullOrWhiteSpace(playlistId)
                ? YouTubeUrlParseResult.UnknownYouTube
                : new YouTubeUrlParseResult(YouTubeUrlKind.Playlist, PlaylistId: playlistId);
        }

        if (firstSegment.Equals("channel", StringComparison.OrdinalIgnoreCase)
            || firstSegment.Equals("c", StringComparison.OrdinalIgnoreCase)
            || firstSegment.StartsWith('@'))
        {
            return new YouTubeUrlParseResult(YouTubeUrlKind.Channel, ChannelPath: uri.AbsolutePath.Trim('/'));
        }

        return YouTubeUrlParseResult.UnknownYouTube;
    }

    private static bool IsSupportedScheme(Uri uri)
    {
        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsYouTubeHost(string host)
    {
        return host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("www.youtube.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("m.youtube.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetPathSegment(Uri uri, int index)
    {
        var segments = uri.AbsolutePath.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
        return index < segments.Length ? SafeUnescape(segments[index]) : null;
    }

    private static string? GetQueryValue(string query, string key)
    {
        var trimmedQuery = query.TrimStart('?');
        if (trimmedQuery.Length == 0)
        {
            return null;
        }

        foreach (var pair in trimmedQuery.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=', StringComparison.Ordinal);
            var rawKey = separatorIndex >= 0 ? pair[..separatorIndex] : pair;
            if (!SafeUnescape(rawKey).Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rawValue = separatorIndex >= 0 ? pair[(separatorIndex + 1)..] : string.Empty;
            return SafeUnescape(rawValue.Replace('+', ' '));
        }

        return null;
    }

    private static string SafeUnescape(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    private static bool IsValidVideoId(string videoId)
    {
        return videoId.Length == 11
            && videoId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    }
}
