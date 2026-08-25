namespace SilverScreen.Infrastructure.YouTube;

public sealed record YtDlpOptions
{
    public string ExecutablePath { get; init; } = "yt-dlp";
    public TimeSpan Timeout { get; } = TimeSpan.FromSeconds(30);
}