using System.Text.Json.Serialization;
using SilverScreen.Core.Common;
using SilverScreen.Core.Downloads;
using VideoQualityEnum = SilverScreen.Core.Preferences.VideoQuality;

namespace SilverScreen.Core.Preferences;

public sealed record AppPreferences
{
    public const int DefaultMaxConcurrentDownloads = 2;
    public const int MinConcurrentDownloads = 1;
    public const int MaxConcurrentDownloadsLimit = 8;

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

    [JsonPropertyName("PreferredCodec")]
    [JsonConverter(typeof(PreferredVideoCodecConverter))]
    public PreferredVideoCodec PreferredCodec { get; set; } = PreferredVideoCodec.Any;

    public string PreferredSubtitleLanguage { get; set; } = string.Empty;
    public string YtDlpExecutablePath { get; set; } = "yt-dlp";
    public bool MarkWatchedVideos { get; set; }
    public bool YouTubePlaybackTelemetryEnabled { get; set; }
    public bool DiscordRichPresenceEnabled { get; set; }
    public bool RydEnabled { get; set; }
    public bool SponsorBlockAutoSkipEnabled { get; set; }
    public bool SponsorBlockSegmentDisplayEnabled { get; set; } = true;
    public bool ResumePlaybackAutomatically { get; set; }
    public bool ResumePlaybackOnDemand { get; set; }
    public bool ShortcutOsdEnabled { get; set; } = true;
    public PlayerShortcutBindings Shortcuts { get; set; } = new();
    public int WindowWidth { get; set; } = 1180;
    public int WindowHeight { get; set; } = 760;
    public bool WindowMaximized { get; set; }

    public EquatableArray<string> SponsorBlockCategories { get; set; } =
        [.. Player.SponsorBlockCategories.All];

    public string LibraryDirectory { get; set; } = GetDefaultLibraryDirectory();
    public int MaxConcurrentDownloads { get; set; } = DefaultMaxConcurrentDownloads;
    public bool ShowCustomDownloadOption { get; set; } = true;
    public EquatableArray<DownloadPreset> DownloadPresets { get; set; } = EquatableArray<DownloadPreset>.Empty;

    public static string GetDefaultLibraryDirectory()
    {
        var videosDir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (string.IsNullOrWhiteSpace(videosDir))
        {
            var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            videosDir = string.IsNullOrWhiteSpace(userHome)
                ? Path.GetTempPath()
                : Path.Combine(userHome, "Videos");
        }

        return Path.Combine(videosDir, "SilverScreen");
    }
}