using SilverScreen.Core.Preferences;

namespace SilverScreen.Infrastructure.Player;

public sealed class PlaybackOptions
{
    public string MpvExecutablePath { get; init; } = "mpv";
    public string YtDlpExecutablePath { get; init; } = "yt-dlp";


    public bool ExternalMpvEnabled { get; init; } = true;

    public string VideoQuality { get; init; } = "Best";
    public PreferredVideoCodec PreferredCodec { get; init; } = PreferredVideoCodec.Any;

    public bool MarkWatchedVideos { get; init; }
    public bool Fullscreen { get; init; } = true;
    public bool AutoAdvanceNextVideo { get; init; } = true;
}