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
        return string.IsNullOrWhiteSpace(videoId)
            ? null
            : $"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}";
    }
}
