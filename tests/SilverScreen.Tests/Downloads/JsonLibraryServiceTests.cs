using SilverScreen.Core.Domain;
using SilverScreen.Core.Downloads;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Downloads;
using SilverScreen.Infrastructure.Preferences;

namespace SilverScreen.Tests.Downloads;

public sealed class JsonLibraryServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _prefsPath;
    private readonly string _libraryDirA;
    private readonly string _libraryDirB;

    public JsonLibraryServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"silverscreen-lib-test-{Guid.NewGuid():N}");
        _prefsPath = Path.Combine(_tempRoot, "preferences.json");
        _libraryDirA = Path.Combine(_tempRoot, "LibraryA");
        _libraryDirB = Path.Combine(_tempRoot, "LibraryB");
        Directory.CreateDirectory(_libraryDirA);
        Directory.CreateDirectory(_libraryDirB);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, true);
        }
        catch
        {
        }
    }

    [Fact]
    public async Task UpsertAsync_PersistsMinifiedHiddenJsonInLibraryFolder_AndRestoresEntry()
    {
        var prefsService = new FilePreferencesService(_prefsPath);
        prefsService.SavePreferences(new AppPreferences { LibraryDirectory = _libraryDirA });

        var mediaFile = Path.Combine(_libraryDirA, "Sample [dQw4w9WgXcQ].mkv");
        await File.WriteAllTextAsync(mediaFile, "video-bytes");

        using var library = new JsonLibraryService(prefsService);
        var video = new VideoSummary("dQw4w9WgXcQ", "Sample", "Channel", TimeSpan.FromMinutes(3), "", false);
        var entry = new LibraryEntry(
            video,
            "Sample [dQw4w9WgXcQ].mkv",
            null,
            DateTimeOffset.UtcNow,
            11,
            DownloadMediaMode.VideoAndAudio,
            VideoQuality.Best,
            1080,
            "1080p",
            "MKV",
            DownloadSubtitleMode.None,
            [new SponsorBlockSegment("seg1", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(25), SponsorBlockCategories.Sponsor)]);

        await library.UpsertAsync(entry);

        var indexPath = Path.Combine(_libraryDirA, JsonLibraryService.IndexFileName);
        Assert.True(File.Exists(indexPath));
        var rawJson = await File.ReadAllTextAsync(indexPath);
        Assert.DoesNotContain("\n", rawJson);

        using var reloaded = new JsonLibraryService(prefsService);
        var loaded = reloaded.TryGetPlayableEntry("dQw4w9WgXcQ");
        Assert.NotNull(loaded);
        Assert.Equal("1080p", loaded.QualityLabel);
        Assert.Equal(1080, loaded.ActualHeight);
        Assert.Single(loaded.SponsorBlockSegments!);
        Assert.Equal(mediaFile, loaded.ToPlayableVideoSummary(_libraryDirA).LocalFilePath);
    }

    [Fact]
    public async Task RemoveAsync_WithDeleteFile_RemovesIndexEntryAndMediaFile()
    {
        var prefsService = new FilePreferencesService(_prefsPath);
        prefsService.SavePreferences(new AppPreferences { LibraryDirectory = _libraryDirA });

        var mediaFile = Path.Combine(_libraryDirA, "Sample [dQw4w9WgXcQ].mp4");
        await File.WriteAllTextAsync(mediaFile, "video-bytes");

        using var library = new JsonLibraryService(prefsService);
        var video = new VideoSummary("dQw4w9WgXcQ", "Sample", "Channel", TimeSpan.FromMinutes(3), "", false);
        await library.UpsertAsync(new LibraryEntry(
            video,
            "Sample [dQw4w9WgXcQ].mp4",
            null,
            DateTimeOffset.UtcNow,
            11,
            DownloadMediaMode.VideoAndAudio,
            VideoQuality.P720,
            720,
            "720p",
            "MP4",
            DownloadSubtitleMode.None));

        Assert.True(library.ContainsPlayable("dQw4w9WgXcQ"));

        await library.RemoveAsync("dQw4w9WgXcQ", deleteFileFromDisk: true);

        Assert.False(library.ContainsPlayable("dQw4w9WgXcQ"));
        Assert.False(File.Exists(mediaFile));
    }

    [Fact]
    public async Task PreferencesChanged_LibraryDirectoryChange_SwitchesActiveIndex()
    {
        var prefsService = new FilePreferencesService(_prefsPath);
        prefsService.SavePreferences(new AppPreferences { LibraryDirectory = _libraryDirA });

        var fileA = Path.Combine(_libraryDirA, "A [vid11111111].mp4");
        await File.WriteAllTextAsync(fileA, "a");

        using var library = new JsonLibraryService(prefsService);
        await library.UpsertAsync(new LibraryEntry(
            new VideoSummary("vid11111111", "A", "Chan", TimeSpan.FromMinutes(1), "", false),
            "A [vid11111111].mp4",
            null,
            DateTimeOffset.UtcNow,
            1,
            DownloadMediaMode.VideoAndAudio,
            VideoQuality.P1080,
            1080,
            "1080p",
            "MP4",
            DownloadSubtitleMode.None));

        Assert.Single(library.Entries);

        var changedRaised = false;
        library.LibraryChanged += (_, _) => changedRaised = true;
        prefsService.SavePreferences(new AppPreferences { LibraryDirectory = _libraryDirB });

        Assert.True(changedRaised);
        Assert.Equal(_libraryDirB, library.LibraryDirectory);
        Assert.Empty(library.Entries);
    }
}
