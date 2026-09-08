using System.Text.Json.Serialization;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using VideoQualityEnum = SilverScreen.Core.Preferences.VideoQuality;

namespace SilverScreen.Core.Preferences;

public sealed record AppPreferences
{
    [JsonPropertyName("Theme")]
    [JsonConverter(typeof(ThemeModeConverter))]
    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;

    [JsonPropertyName("PlaybackBackend")]
    [JsonConverter(typeof(PlaybackBackendKindConverter))]
    public PlaybackBackendKind PlaybackBackendKind { get; set; } = PlaybackBackendKind.Embedded;

    public bool OpenInFullscreen { get; set; } = true;
    public bool AutoAdvanceNextVideo { get; set; } = true;
    public string MpvExecutablePath { get; set; } = "mpv";

    [JsonPropertyName("VideoQuality")]
    [JsonConverter(typeof(VideoQualityConverter))]
    public VideoQualityEnum Quality { get; set; } = VideoQualityEnum.Best;

    public string PreferredSubtitleLanguage { get; set; } = string.Empty;
    public string YtDlpExecutablePath { get; set; } = "yt-dlp";
    public bool MarkWatchedVideos { get; set; }
    public bool YouTubePlaybackTelemetryEnabled { get; set; }
    public bool DiscordRichPresenceEnabled { get; set; }
    public bool RydEnabled { get; set; }
    public bool SponsorBlockAutoSkipEnabled { get; set; }
    public bool SponsorBlockSegmentDisplayEnabled { get; set; }
    public bool ResumePlaybackAutomatically { get; set; }
    public bool ResumePlaybackOnDemand { get; set; }
    public bool ShortcutOsdEnabled { get; set; } = true;
    public PlayerShortcutBindings Shortcuts { get; set; } = new();
    public int WindowWidth { get; set; } = 1180;
    public int WindowHeight { get; set; } = 760;
    public bool WindowMaximized { get; set; }

    public EquatableArray<string> SponsorBlockCategories { get; set; } =
        [.. Player.SponsorBlockCategories.All];
}
