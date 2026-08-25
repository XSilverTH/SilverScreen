using System.Reflection;
using System.Text.Json;
using SilverScreen.Core.Common;
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
            Theme = "Dark",
            MpvExecutablePath = "/custom/mpv",
            YtDlpExecutablePath = "/custom/yt-dlp",
            VideoQuality = "1080p",
            PreferredSubtitleLanguage = "en",
            PlaybackBackend = PlaybackBackends.EmbeddedPlayer,
            OpenInFullscreen = false,
            AutoAdvanceNextVideo = false,
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

    [Fact]
    public void EveryAppPreferenceProperty_ParticipatesInEquality_SaveNotification_AndPersistence()
    {
        var properties = typeof(AppPreferences).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, CanWrite: true })
            .ToArray();

        Assert.NotEmpty(properties);

        foreach (var property in properties)
        {
            var tempPath = Path.Combine(Path.GetTempPath(),
                $"silverscreen-prop-test-{property.Name}-{Guid.NewGuid():N}.json");
            try
            {
                var basePrefs = new AppPreferences();
                var modifiedPrefs = new AppPreferences();
                var originalValue = property.GetValue(basePrefs);
                var distinctValue = GetDistinctValue(property.PropertyType, property.Name, originalValue);

                property.SetValue(modifiedPrefs, distinctValue);

                // 1. Value equality must detect the difference without manual method bodies
                Assert.False(basePrefs.Equals(modifiedPrefs),
                    $"Property '{property.Name}' was not considered in AppPreferences.Equals.");
                Assert.False(basePrefs == modifiedPrefs,
                    $"Property '{property.Name}' was not considered in AppPreferences operator==.");
                Assert.NotEqual(basePrefs.GetHashCode(), modifiedPrefs.GetHashCode());

                // 2. FilePreferencesService must detect the change and NOT suppress saving
                var service = new FilePreferencesService(tempPath);
                service.SavePreferences(basePrefs);

                var eventsRaised = 0;
                service.PreferencesChanged += (_, _) => eventsRaised++;

                service.SavePreferences(modifiedPrefs);
                Assert.Equal(1, eventsRaised);

                // 3. The persisted file on disk must reload the modified value
                var secondService = new FilePreferencesService(tempPath);
                var loaded = secondService.GetPreferences();
                var loadedValue = property.GetValue(loaded);

                Assert.Equal(distinctValue, loadedValue);
                Assert.Equal(modifiedPrefs, loaded);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public void EveryPlayerShortcutProperty_ParticipatesInEquality_AndPersistence()
    {
        var properties = typeof(PlayerShortcutBindings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, CanWrite: true })
            .ToArray();

        Assert.NotEmpty(properties);

        foreach (var property in properties)
        {
            var tempPath = Path.Combine(Path.GetTempPath(),
                $"silverscreen-shortcut-prop-test-{property.Name}-{Guid.NewGuid():N}.json");
            try
            {
                var baseShortcuts = new PlayerShortcutBindings();
                var modifiedShortcuts = new PlayerShortcutBindings();
                var distinctShortcuts = new EquatableArray<string>(["UniqueCustomKey_" + property.Name]);

                property.SetValue(modifiedShortcuts, distinctShortcuts);

                // 1. Equality and HashCode
                Assert.False(baseShortcuts.Equals(modifiedShortcuts),
                    $"Shortcut property '{property.Name}' was not considered in PlayerShortcutBindings.Equals.");
                Assert.False(baseShortcuts == modifiedShortcuts,
                    $"Shortcut property '{property.Name}' was not considered in PlayerShortcutBindings operator==.");
                Assert.NotEqual(baseShortcuts.GetHashCode(), modifiedShortcuts.GetHashCode());

                // 2. Persistence through AppPreferences
                var basePrefs = new AppPreferences { Shortcuts = baseShortcuts };
                var modifiedPrefs = new AppPreferences { Shortcuts = modifiedShortcuts };

                var service = new FilePreferencesService(tempPath);
                service.SavePreferences(basePrefs);

                var eventsRaised = 0;
                service.PreferencesChanged += (_, _) => eventsRaised++;

                service.SavePreferences(modifiedPrefs);
                Assert.Equal(1, eventsRaised);

                var loaded = new FilePreferencesService(tempPath).GetPreferences();
                var loadedShortcutValue = property.GetValue(loaded.Shortcuts);

                Assert.Equal(distinctShortcuts, loadedShortcutValue);
                Assert.Equal(modifiedPrefs, loaded);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public void LoadPreferences_FromExistingPreferencesJsonFile_LoadsAllPropertiesAccurately()
    {
        const string existingJson = """
                                    {
                                      "Theme": "Dark",
                                      "PlaybackBackend": "embedded-player",
                                      "OpenInFullscreen": false,
                                      "AutoAdvanceNextVideo": false,
                                      "MpvExecutablePath": "/usr/bin/mpv",
                                      "VideoQuality": "720p",
                                      "PreferredSubtitleLanguage": "ja",
                                      "YtDlpExecutablePath": "/usr/bin/yt-dlp",
                                      "MarkWatchedVideos": true,
                                      "YouTubePlaybackTelemetryEnabled": false,
                                      "DiscordRichPresenceEnabled": true,
                                      "SponsorBlockAutoSkipEnabled": true,
                                      "SponsorBlockSegmentDisplayEnabled": true,
                                      "ResumePlaybackAutomatically": true,
                                      "ResumePlaybackOnDemand": false,
                                      "Shortcuts": {
                                        "TogglePause": ["space", "p"],
                                        "SeekBackward": ["Left", "h"],
                                        "SeekForward": ["Right", "l"],
                                        "StepFrameBackward": ["comma"],
                                        "StepFrameForward": ["period"],
                                        "ToggleMute": ["m"],
                                        "VolumeUp": ["Up"],
                                        "VolumeDown": ["Down"],
                                        "SeekToBeginning": ["Home"],
                                        "ReturnToShell": ["Escape"],
                                        "ToggleVideoInfo": ["i"],
                                        "SpeedDecrease": ["[", "{"],
                                        "SpeedIncrease": ["]", "}"],
                                        "NextVideo": [">"],
                                        "PreviousVideo": ["<"],
                                        "ToggleFullscreen": ["f"],
                                        "PreferredSubtitle": ["s"],
                                        "ResumeOrSkip": ["Return"],
                                        "ToggleQueue": ["q"]
                                      },
                                      "SponsorBlockCategories": ["sponsor", "selfpromo", "outro"]
                                    }
                                    """;

        File.WriteAllText(_tempFilePath, existingJson);

        var service = new FilePreferencesService(_tempFilePath);
        var loaded = service.GetPreferences();

        Assert.Equal("Dark", loaded.Theme);
        Assert.Equal("embedded-player", loaded.PlaybackBackend);
        Assert.False(loaded.OpenInFullscreen);
        Assert.False(loaded.AutoAdvanceNextVideo);
        Assert.Equal("/usr/bin/mpv", loaded.MpvExecutablePath);
        Assert.Equal("720p", loaded.VideoQuality);
        Assert.Equal("ja", loaded.PreferredSubtitleLanguage);
        Assert.Equal("/usr/bin/yt-dlp", loaded.YtDlpExecutablePath);
        Assert.True(loaded.MarkWatchedVideos);
        Assert.False(loaded.YouTubePlaybackTelemetryEnabled);
        Assert.True(loaded.DiscordRichPresenceEnabled);
        Assert.True(loaded.SponsorBlockAutoSkipEnabled);
        Assert.True(loaded.SponsorBlockSegmentDisplayEnabled);
        Assert.True(loaded.ResumePlaybackAutomatically);
        Assert.False(loaded.ResumePlaybackOnDemand);

        Assert.Equal(["space", "p"], loaded.Shortcuts.TogglePause);
        Assert.Equal(["Left", "h"], loaded.Shortcuts.SeekBackward);
        Assert.Equal(["Right", "l"], loaded.Shortcuts.SeekForward);
        Assert.Equal(["comma"], loaded.Shortcuts.StepFrameBackward);
        Assert.Equal(["period"], loaded.Shortcuts.StepFrameForward);
        Assert.Equal(["m"], loaded.Shortcuts.ToggleMute);
        Assert.Equal(["Up"], loaded.Shortcuts.VolumeUp);
        Assert.Equal(["Down"], loaded.Shortcuts.VolumeDown);
        Assert.Equal(["Home"], loaded.Shortcuts.SeekToBeginning);
        Assert.Equal(["Escape"], loaded.Shortcuts.ReturnToShell);
        Assert.Equal(["i"], loaded.Shortcuts.ToggleVideoInfo);
        Assert.Equal(["[", "{"], loaded.Shortcuts.SpeedDecrease);
        Assert.Equal(["]", "}"], loaded.Shortcuts.SpeedIncrease);
        Assert.Equal([">"], loaded.Shortcuts.NextVideo);
        Assert.Equal(["<"], loaded.Shortcuts.PreviousVideo);
        Assert.Equal(["f"], loaded.Shortcuts.ToggleFullscreen);
        Assert.Equal(["s"], loaded.Shortcuts.PreferredSubtitle);
        Assert.Equal(["Return"], loaded.Shortcuts.ResumeOrSkip);
        Assert.Equal(["q"], loaded.Shortcuts.ToggleQueue);

        Assert.Equal(["sponsor", "selfpromo", "outro"], loaded.SponsorBlockCategories);
    }

    [Fact]
    public void SavePreferences_ExternalMutation_DoesNotCorruptServiceState()
    {
        var service = new FilePreferencesService(_tempFilePath);
        var original = new AppPreferences
        {
            Theme = "Light",
            Shortcuts = new PlayerShortcutBindings { TogglePause = ["space"] }
        };

        service.SavePreferences(original);

        // Mutate original instance after saving
        original.Theme = "Dark";
        original.Shortcuts.TogglePause = ["mutated"];

        var current = service.GetPreferences();
        Assert.Equal("Light", current.Theme);
        Assert.Equal(["space"], current.Shortcuts.TogglePause);

        // Mutate retrieved instance
        current.Theme = "Dark";
        current.Shortcuts.TogglePause = ["mutated_again"];

        var currentAgain = service.GetPreferences();
        Assert.Equal("Light", currentAgain.Theme);
        Assert.Equal(["space"], currentAgain.Shortcuts.TogglePause);
    }

    private static object GetDistinctValue(Type type, string propertyName, object? currentValue)
    {
        if (type == typeof(string))
            return $"custom_{propertyName}_val";
        if (type == typeof(bool))
            return !(bool)(currentValue ?? false);
        if (type == typeof(int))
            return (int)(currentValue ?? 0) + 42;
        if (type == typeof(EquatableArray<string>))
            return new EquatableArray<string>(["custom_cat_1", "custom_cat_2"]);
        return type == typeof(PlayerShortcutBindings)
            ? new PlayerShortcutBindings { TogglePause = ["CustomTestKey"] }
            : throw new NotSupportedException($"Add test support for type {type.Name}");
    }
}