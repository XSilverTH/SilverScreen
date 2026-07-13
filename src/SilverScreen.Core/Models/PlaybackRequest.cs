namespace SilverScreen.Core.Models;

public sealed record PlaybackRequest(VideoSummary Video)
{
    public string VideoId => Video.Id;

    public string Title => Video.Title;

    public string? PlaybackUrl => string.IsNullOrWhiteSpace(Video.WatchUrl)
        ? BuildWatchUrl(Video.Id)
        : Video.WatchUrl;

    public static string? BuildWatchUrl(string videoId)
    {
        return LooksLikeYouTubeVideoId(videoId)
            ? $"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}"
            : null;
    }

    public static bool LooksLikeYouTubeVideoId(string id)
    {
        return id.Length == 11
               && !id.StartsWith("Ss", StringComparison.Ordinal)
               && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    }
}