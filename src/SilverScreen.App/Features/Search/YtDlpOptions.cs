namespace SilverScreen.Features.Search;

public sealed record YtDlpOptions
{
    public string ExecutablePath { get; init; } = "yt-dlp";

    public int MaxResults { get; init; } = 20;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}
