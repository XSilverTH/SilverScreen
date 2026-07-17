using SilverScreen.Core.Models;
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
        if (File.Exists(_tempFilePath))
        {
            try
            {
                File.Delete(_tempFilePath);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }

    [Fact]
    public void GetPreferences_ReturnsDefaultPreferences_WhenFileDoesNotExist()
    {
        var service = new FilePreferencesService(_tempFilePath);
        var prefs = service.GetPreferences();

        Assert.NotNull(prefs);
        Assert.Equal("System", prefs.Theme);
        Assert.Equal("mpv", prefs.MpvExecutablePath);
        Assert.Equal("yt-dlp", prefs.YtDlpExecutablePath);
        Assert.Equal("Best", prefs.VideoQuality);
        Assert.Equal(20, prefs.MaxResults);
        Assert.False(prefs.MarkWatchedVideos);
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
            MaxResults = 50,
            MarkWatchedVideos = true
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
        Assert.Equal(50, loaded.MaxResults);
        Assert.True(loaded.MarkWatchedVideos);
    }

    [Fact]
    public void SavePreferences_RaisesPreferencesChangedEvent()
    {
        var service = new FilePreferencesService(_tempFilePath);
        var newPrefs = new AppPreferences
        {
            Theme = "Light",
            MpvExecutablePath = "mpv",
            YtDlpExecutablePath = "yt-dlp",
            VideoQuality = "720p",
            MaxResults = 10,
            MarkWatchedVideos = true
        };

        AppPreferences? eventArgs = null;
        service.PreferencesChanged += (sender, args) =>
        {
            eventArgs = args;
        };

        service.SavePreferences(newPrefs);

        Assert.NotNull(eventArgs);
        Assert.Equal("Light", eventArgs.Theme);
        Assert.Equal("720p", eventArgs.VideoQuality);
        Assert.Equal(10, eventArgs.MaxResults);
        Assert.True(eventArgs.MarkWatchedVideos);
    }
}
