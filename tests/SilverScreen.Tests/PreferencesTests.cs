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
            ResumePlaybackAutomatically = true,
            ResumePlaybackOnDemand = false,
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
        Assert.Equal(["Pause"], loaded.Shortcuts.TogglePause);
        Assert.Equal(["A"], loaded.Shortcuts.SeekBackward);
        Assert.False(loaded.SponsorBlockSegmentDisplayEnabled);
        Assert.True(loaded.ResumePlaybackAutomatically);
        Assert.False(loaded.ResumePlaybackOnDemand);
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
    public void SavePreferences_WhenOnlyResumePlaybackOnDemandChanges_PersistsAndRaisesEvent()
    {
        var service = new FilePreferencesService(_tempFilePath);
        service.SavePreferences(new AppPreferences { ResumePlaybackOnDemand = false });

        var events = 0;
        service.PreferencesChanged += (_, _) => events++;

        service.SavePreferences(new AppPreferences { ResumePlaybackOnDemand = true });

        Assert.Equal(1, events);
        var secondService = new FilePreferencesService(_tempFilePath);
        Assert.True(secondService.GetPreferences().ResumePlaybackOnDemand);
    }

    [Fact]
    public void LoadPreferences_MissingShortcuts_UsesCurrentDefaults()
    {
        File.WriteAllText(_tempFilePath, "{}");

        var service = new FilePreferencesService(_tempFilePath);

        Assert.Equal(["space", "k"], service.GetPreferences().Shortcuts.TogglePause);
        Assert.Equal(["Return", "KP_Enter"], service.GetPreferences().Shortcuts.ResumeOrSkip);
    }

    [Fact]
    public void GetPreferences_ClonesShortcutBindings()
    {
        var service = new FilePreferencesService(_tempFilePath);
        service.SavePreferences(new AppPreferences
        {
            Shortcuts = new PlayerShortcutBindings { TogglePause = ["Pause"] }
        });

        var loaded = service.GetPreferences();
        loaded.Shortcuts.TogglePause[0] = "Changed";

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