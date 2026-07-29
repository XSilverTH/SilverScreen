using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Preferences;

namespace SilverScreen.Tests;

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
    public void SavePreferences_PersistsPreferences_AndLoadsThemCorrectly()
    {
        var service = new FilePreferencesService(_tempFilePath);
        var newPrefs = new AppPreferences
        {
            Theme = "Dark",
            MpvExecutablePath = "/custom/mpv",
            YtDlpExecutablePath = "/custom/yt-dlp",
            VideoQuality = "1080p",
            PreferredSubtitleLanguage = "en",
            PlaybackBackend = PlaybackBackends.EmbeddedPlayer,
            OpenInFullscreen = false,
            AutoAdvanceNextVideo = false,
            MaxResults = 50,
            MarkWatchedVideos = true,
            DiscordRichPresenceEnabled = true,
            SponsorBlockAutoSkipEnabled = true,
            SponsorBlockSegmentDisplayEnabled = false,
            SponsorBlockCategories = [SponsorBlockCategories.Sponsor, SponsorBlockCategories.Outro]
        };

        service.SavePreferences(newPrefs);

        // Create a new service instance reading from the same file to verify persistence
        var secondService = new FilePreferencesService(_tempFilePath);
        var loaded = secondService.GetPreferences();

        Assert.NotNull(loaded);
        Assert.Equal("Dark", loaded.Theme);
        Assert.Equal("/custom/mpv", loaded.MpvExecutablePath);
        Assert.Equal("/custom/yt-dlp", loaded.YtDlpExecutablePath);
        Assert.Equal("1080p", loaded.VideoQuality);
        Assert.Equal("en", loaded.PreferredSubtitleLanguage);
        Assert.Equal(PlaybackBackends.EmbeddedPlayer, loaded.PlaybackBackend);
        Assert.False(loaded.OpenInFullscreen);
        Assert.False(loaded.AutoAdvanceNextVideo);
        Assert.Equal(50, loaded.MaxResults);
        Assert.True(loaded.MarkWatchedVideos);
        Assert.True(loaded.DiscordRichPresenceEnabled);
        Assert.True(loaded.SponsorBlockAutoSkipEnabled);
        Assert.False(loaded.SponsorBlockSegmentDisplayEnabled);
        Assert.Equal([SponsorBlockCategories.Sponsor, SponsorBlockCategories.Outro], loaded.SponsorBlockCategories);
    }

    [Fact]
    public void AppPreferences_SponsorBlockDefaults_ShowAllSegmentCategoriesWithoutAutoSkip()
    {
        var preferences = new AppPreferences();

        Assert.False(preferences.SponsorBlockAutoSkipEnabled);
        Assert.True(preferences.SponsorBlockSegmentDisplayEnabled);
        Assert.Equal(SponsorBlockCategories.All, preferences.SponsorBlockCategories);
    }


    [Fact]
    public void SavePreferences_TelemetryTakesPrecedenceOverMarkWatched()
    {
        var service = new FilePreferencesService(_tempFilePath);

        service.SavePreferences(new AppPreferences
        {
            MarkWatchedVideos = true,
            YouTubePlaybackTelemetryEnabled = true
        });

        var loaded = new FilePreferencesService(_tempFilePath).GetPreferences();

        Assert.True(loaded.YouTubePlaybackTelemetryEnabled);
        Assert.False(loaded.MarkWatchedVideos);
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
            service.SavePreferences(new AppPreferences { Theme = "Dark" }));

        Assert.Equal(_tempFilePath, exception.FilePath);
        Assert.True(Directory.Exists(_tempFilePath));
        Assert.Equal(original.Theme, service.GetPreferences().Theme);
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

        service.SavePreferences(new AppPreferences { Theme = "Dark" });

        Assert.Equal(1, events);
        Assert.True(File.Exists(_tempFilePath));
    }
}