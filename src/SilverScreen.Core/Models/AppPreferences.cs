namespace SilverScreen.Core.Models;

public sealed class AppPreferences
{
    public string Theme { get; set; } = "System"; // "System", "Light", "Dark"
    public string MpvExecutablePath { get; set; } = "mpv";
    public string VideoQuality { get; set; } = "Best"; // "Best", "1080p", "720p", "480p", "360p"
    public string YtDlpExecutablePath { get; set; } = "yt-dlp";
    public int MaxResults { get; set; } = 20;
    public bool MarkWatchedVideos { get; set; }
}
