namespace SilverScreen.Infrastructure.Player;

public sealed class PlaybackOptions
{
    public string MpvExecutablePath { get; init; } = "mpv";

    public bool ExternalMpvEnabled { get; init; } = true;

    public string VideoQuality { get; init; } = "Best";

    public bool MarkWatchedVideos { get; init; }
    public bool Fullscreen { get; init; } = true;
    public bool AutoAdvanceNextVideo { get; init; } = true;
}