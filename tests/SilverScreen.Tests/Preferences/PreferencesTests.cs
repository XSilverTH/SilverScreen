using System.Text.Json;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Preferences;

namespace SilverScreen.Tests.Preferences;

public sealed class PreferencesTests : IDisposable
{
    private readonly string _tempFilePath;

    public PreferencesTests()
    {
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"silverscreen-test-prefs-{Guid.NewGuid()}.json");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_tempFilePath))
                File.Delete(_tempFilePath);
            else if (Directory.Exists(_tempFilePath))
                Directory.Delete(_tempFilePath);
        }
        catch
        {
            // Ignore cleanup errors in tests
        }
    }

    [Fact]
    public void AppPreferences_JsonSerialization_PreservesEquatableArrayProperties()
    {
        var prefs = new AppPreferences
        {
            SponsorBlockCategories = ["sponsor", "intro"],
            Shortcuts = new PlayerShortcutBindings
            {
                TogglePause = ["space", "p"]
            }
        };

        var json = JsonSerializer.Serialize(prefs, PreferencesJsonContext.Default.AppPreferences);
        var deserialized = JsonSerializer.Deserialize(json, PreferencesJsonContext.Default.AppPreferences);

        Assert.NotNull(deserialized);
        Assert.Equal(["sponsor", "intro"], deserialized.SponsorBlockCategories);
        Assert.Equal(["space", "p"], deserialized.Shortcuts.TogglePause);
    }

    [Fact]
    public void SavePreferences_PersistsPreferences_AndLoadsThemCorrectly()
    {
        var service = new FilePreferencesService(_tempFilePath);
        var newPrefs = new AppPreferences
        {
            ThemeMode = ThemeMode.Dark,
            MpvExecutablePath = "/custom/mpv",
            YtDlpExecutablePath = "/custom/yt-dlp",
            Quality = VideoQuality.P1080,
            PreferredSubtitleLanguage = "en",
            PlaybackBackendKind = PlaybackBackendKind.Embedded,
            OpenInFullscreen = false,
            AutoAdvanceNextVideo = false,
            MarkWatchedVideos = true,
            DiscordRichPresenceEnabled = true,
            SponsorBlockAutoSkipEnabled = true,
            SponsorBlockSegmentDisplayEnabled = false,
            ResumePlaybackAutomatically = true,
            ResumePlaybackOnDemand = false,
            ShortcutOsdEnabled = false,
            Shortcuts = new PlayerShortcutBindings
            {
                TogglePause = ["Pause"],
                SeekBackward = ["A"]
            },

            SponsorBlockCategories = [SponsorBlockCategories.Sponsor, SponsorBlockCategories.Outro]
        };

        service.SavePreferences(newPrefs);

        // Create a new service instance reading from the same file to verify persistence
        var secondService = new FilePreferencesService(_tempFilePath);
        var loaded = secondService.GetPreferences();

        Assert.NotNull(loaded);
        Assert.Equal(ThemeMode.Dark, loaded.ThemeMode);
        Assert.Equal("/custom/mpv", loaded.MpvExecutablePath);
        Assert.Equal("/custom/yt-dlp", loaded.YtDlpExecutablePath);
        Assert.Equal(VideoQuality.P1080, loaded.Quality);
        Assert.Equal("en", loaded.PreferredSubtitleLanguage);
        Assert.Equal(PlaybackBackendKind.Embedded, loaded.PlaybackBackendKind);
        Assert.False(loaded.OpenInFullscreen);
        Assert.False(loaded.AutoAdvanceNextVideo);
        Assert.True(loaded.MarkWatchedVideos);
        Assert.True(loaded.DiscordRichPresenceEnabled);
        Assert.True(loaded.SponsorBlockAutoSkipEnabled);
        Assert.Equal(["Pause"], loaded.Shortcuts.TogglePause);
        Assert.Equal(["A"], loaded.Shortcuts.SeekBackward);
        Assert.False(loaded.SponsorBlockSegmentDisplayEnabled);
        Assert.True(loaded.ResumePlaybackAutomatically);
        Assert.False(loaded.ResumePlaybackOnDemand);
        Assert.False(loaded.ShortcutOsdEnabled);
        Assert.Equal([SponsorBlockCategories.Sponsor, SponsorBlockCategories.Outro], loaded.SponsorBlockCategories);
    }

    [Fact]
    public void SavePreferences_PersistsConflictingFlags_WithoutSilentlyMutatingThem()
    {
        var service = new FilePreferencesService(_tempFilePath);
        var conflicting = new AppPreferences
        {
            MarkWatchedVideos = true,
            YouTubePlaybackTelemetryEnabled = true,
            ResumePlaybackAutomatically = true,
            ResumePlaybackOnDemand = true
        };

        service.SavePreferences(conflicting);

        var inMemory = service.GetPreferences();
        Assert.True(inMemory.MarkWatchedVideos);
        Assert.True(inMemory.YouTubePlaybackTelemetryEnabled);
        Assert.True(inMemory.ResumePlaybackAutomatically);
        Assert.True(inMemory.ResumePlaybackOnDemand);

        var secondService = new FilePreferencesService(_tempFilePath);
        var loaded = secondService.GetPreferences();
        Assert.True(loaded.MarkWatchedVideos);
        Assert.True(loaded.YouTubePlaybackTelemetryEnabled);
        Assert.True(loaded.ResumePlaybackAutomatically);
        Assert.True(loaded.ResumePlaybackOnDemand);
    }

    [Fact]
    public void LoadPreferences_MissingShortcuts_UsesCurrentDefaults()
    {
        File.WriteAllText(_tempFilePath, "{}");

        var service = new FilePreferencesService(_tempFilePath);

        Assert.Equal(["space", "k"], service.GetPreferences().Shortcuts.TogglePause);
        Assert.Equal(["i", "I"], service.GetPreferences().Shortcuts.ToggleStats);
        Assert.Equal(["Return", "KP_Enter"], service.GetPreferences().Shortcuts.ResumeOrSkip);
    }

    [Fact]
    public void GetPreferences_ReturnsImmutableShortcutBindings_PreservingServiceState()
    {
        var service = new FilePreferencesService(_tempFilePath);
        service.SavePreferences(new AppPreferences
        {
            Shortcuts = new PlayerShortcutBindings { TogglePause = ["Pause"] }
        });

        var loaded = service.GetPreferences();
        var modified = loaded with
        {
            Shortcuts = loaded.Shortcuts with { TogglePause = ["Changed"] }
        };

        Assert.Equal(["Changed"], modified.Shortcuts.TogglePause);
        Assert.Equal(["Pause"], service.GetPreferences().Shortcuts.TogglePause);
    }

    [Fact]
    public void SavePreferences_ThrowsAndKeepsCurrentPreferences_WhenAtomicReplacementFails()
    {
        Directory.CreateDirectory(_tempFilePath);
        var service = new FilePreferencesService(_tempFilePath);
        var original = service.GetPreferences();
        var eventRaised = false;
        service.PreferencesChanged += (_, _) => eventRaised = true;

        var exception = Assert.Throws<PreferencesPersistenceException>(() =>
            service.SavePreferences(new AppPreferences { ThemeMode = ThemeMode.Dark }));

        Assert.Equal(_tempFilePath, exception.FilePath);
        Assert.True(Directory.Exists(_tempFilePath));
        Assert.Equal(original.ThemeMode, service.GetPreferences().ThemeMode);
        Assert.False(eventRaised);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(_tempFilePath)!,
            $".{Path.GetFileName(_tempFilePath)}.*.tmp"));
    }

    [Fact]
    public void SavePreferences_UnchangedClone_DoesNotRaiseEventOrWriteFile()
    {
        var service = new FilePreferencesService(_tempFilePath);
        var events = 0;
        service.PreferencesChanged += (_, _) => events++;

        service.SavePreferences(service.GetPreferences());

        Assert.Equal(0, events);
        Assert.False(File.Exists(_tempFilePath));
    }

    [Fact]
    public void SavePreferences_ChangedProperty_RaisesOneEvent()
    {
        var service = new FilePreferencesService(_tempFilePath);
        var events = 0;
        service.PreferencesChanged += (_, _) => events++;

        service.SavePreferences(new AppPreferences { ThemeMode = ThemeMode.Dark });

        Assert.Equal(1, events);
        Assert.True(File.Exists(_tempFilePath));
    }

    [Fact]
    public void SavePreferences_ExternalMutation_DoesNotCorruptServiceState()
    {
        var service = new FilePreferencesService(_tempFilePath);
        var original = new AppPreferences
        {
            ThemeMode = ThemeMode.Light,
            Shortcuts = new PlayerShortcutBindings { TogglePause = ["space"] }
        };

        service.SavePreferences(original);

        // Mutate original instance after saving
        original.ThemeMode = ThemeMode.Dark;
        original.Shortcuts.TogglePause = ["mutated"];

        var current = service.GetPreferences();
        Assert.Equal(ThemeMode.Light, current.ThemeMode);
        Assert.Equal(["space"], current.Shortcuts.TogglePause);

        // Mutate retrieved instance
        current.ThemeMode = ThemeMode.Dark;
        current.Shortcuts.TogglePause = ["mutated_again"];

        var currentAgain = service.GetPreferences();
        Assert.Equal(ThemeMode.Light, currentAgain.ThemeMode);
        Assert.Equal(["space"], currentAgain.Shortcuts.TogglePause);
    }

    [Theory]
    [InlineData("4320p", VideoQuality.P4320)]
    [InlineData("2160p", VideoQuality.P2160)]
    [InlineData("1440p", VideoQuality.P1440)]
    [InlineData("1080p", VideoQuality.P1080)]
    [InlineData("720p", VideoQuality.P720)]
    [InlineData("480p", VideoQuality.P480)]
    [InlineData("360p", VideoQuality.P360)]
    [InlineData("240p", VideoQuality.P240)]
    [InlineData("144p", VideoQuality.P144)]
    [InlineData("Best", VideoQuality.Best)]
    [InlineData("4K", VideoQuality.P2160)]
    [InlineData("2K", VideoQuality.P1440)]
    [InlineData("8K", VideoQuality.P4320)]
    public void VideoQualities_Parse_MapsExpectedValues(string input, VideoQuality expected)
    {
        Assert.Equal(expected, VideoQualities.Parse(input));
    }

    [Theory]
    [InlineData("Any", PreferredVideoCodec.Any)]
    [InlineData("AV1", PreferredVideoCodec.Av1)]
    [InlineData("VP9", PreferredVideoCodec.Vp9)]
    [InlineData("H.264", PreferredVideoCodec.H264)]
    [InlineData("av01", PreferredVideoCodec.Av1)]
    [InlineData("vp09", PreferredVideoCodec.Vp9)]
    [InlineData("h264", PreferredVideoCodec.H264)]
    [InlineData("AVC", PreferredVideoCodec.H264)]
    public void PreferredVideoCodecs_Parse_MapsExpectedValues(string input, PreferredVideoCodec expected)
    {
        Assert.Equal(expected, PreferredVideoCodecs.Parse(input));
    }

    [Fact]
    public void SavePreferences_WithNewResolutionsAndPreferredCodec_PersistsAndLoadsCorrectly()
    {
        var service = new FilePreferencesService(_tempFilePath);
        var prefs = new AppPreferences
        {
            Quality = VideoQuality.P2160,
            PreferredCodec = PreferredVideoCodec.Vp9
        };

        service.SavePreferences(prefs);

        var loaded = new FilePreferencesService(_tempFilePath).GetPreferences();
        Assert.Equal(VideoQuality.P2160, loaded.Quality);
        Assert.Equal(PreferredVideoCodec.Vp9, loaded.PreferredCodec);
    }
}