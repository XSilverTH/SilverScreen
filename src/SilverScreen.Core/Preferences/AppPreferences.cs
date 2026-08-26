using SilverScreen.Core.Common;
using SilverScreen.Core.Player;

namespace SilverScreen.Core.Preferences;

public sealed record AppPreferences
{
    public string Theme { get; set; } = "System"; // "System", "Light", "Dark"
    public string PlaybackBackend { get; set; } = PlaybackBackends.ExternalMpv;
    public bool OpenInFullscreen { get; set; } = true;
    public bool AutoAdvanceNextVideo { get; set; } = true;
    public string MpvExecutablePath { get; set; } = "mpv";
    public string VideoQuality { get; set; } = "Best"; // "Best", "1080p", "720p", "480p", "360p"
    public string PreferredSubtitleLanguage { get; set; } = string.Empty;
    public string YtDlpExecutablePath { get; set; } = "yt-dlp";
    public bool MarkWatchedVideos { get; set; }
    public bool YouTubePlaybackTelemetryEnabled { get; set; }
    public bool DiscordRichPresenceEnabled { get; set; }
    public bool SponsorBlockAutoSkipEnabled { get; set; }
    public bool SponsorBlockSegmentDisplayEnabled { get; set; } = true;
    public bool ResumePlaybackAutomatically { get; set; }
    public bool ResumePlaybackOnDemand { get; set; }
    public bool ShortcutOsdEnabled { get; set; } = true;
    public PlayerShortcutBindings Shortcuts { get; set; } = new();

    public EquatableArray<string> SponsorBlockCategories { get; set; } =
        [.. Player.SponsorBlockCategories.All];
}