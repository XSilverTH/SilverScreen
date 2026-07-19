using System.Collections.Immutable;

namespace SilverScreen.Core.Models;

public sealed record PlaybackRequest(ImmutableArray<VideoSummary> Videos)
{

    public static string? BuildWatchUrl(string videoId)
    {
        return LooksLikeYouTubeVideoId(videoId)
            ? $"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}"
            : null;
    }

    public static bool LooksLikeYouTubeVideoId(string id)
    {
        return id.Length == 11
               && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    }
}