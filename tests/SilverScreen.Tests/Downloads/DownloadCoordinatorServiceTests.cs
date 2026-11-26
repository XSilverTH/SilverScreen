using SilverScreen.Core.Domain;
using SilverScreen.Core.Downloads;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Downloads;
using SilverScreen.Infrastructure.Preferences;

namespace SilverScreen.Tests.Downloads;

public sealed class DownloadCoordinatorServiceTests : IDisposable
{
    private readonly string _externalDir;
    private readonly string _fakeYtDlpPath;
    private readonly string _libraryDir;
    private readonly string _prefsPath;
    private readonly string _queuePath;
    private readonly string _tempRoot;

    public DownloadCoordinatorServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"silverscreen-dl-test-{Guid.NewGuid():N}");
        _prefsPath = Path.Combine(_tempRoot, "preferences.json");
        _libraryDir = Path.Combine(_tempRoot, "Library");
        _externalDir = Path.Combine(_tempRoot, "External");
        _queuePath = Path.Combine(_tempRoot, "downloads.json");
        _fakeYtDlpPath = Path.Combine(_tempRoot, "fake-yt-dlp.sh");
        Directory.CreateDirectory(_libraryDir);
        Directory.CreateDirectory(_externalDir);
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

    private void WriteFakeYtDlp(string scriptBody)
    {
        File.WriteAllText(_fakeYtDlpPath, $"#!/usr/bin/env bash\nset -e\n{scriptBody}\n");
        File.SetUnixFileMode(_fakeYtDlpPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [Fact]
    public async Task EnqueueAsync_LibraryAndExternalDestinations_IndexesOnlyLibraryAndDeletesSupersededFile()
    {
        WriteFakeYtDlp("""
                       TARGET_DIR=""
                       EXT="mp4"
                       URL=""
                       while [[ $# -gt 0 ]]; do
                         case "$1" in
                           -P) TARGET_DIR="$2"; shift 2 ;;
                           --merge-output-format) EXT="$2"; shift 2 ;;
                           *) URL="$1"; shift ;;
                         esac
                       done
                       VID="${URL##*v=}"
                       OUT="$TARGET_DIR/Test [$VID].$EXT"
                       echo "SILVER_TARGET|$OUT|1080|$EXT"
                       echo "SILVER_DL|downloading|50|100|100|1000|1" >&2
                       printf 'media-content' > "$OUT"
                       echo "SILVER_DL|finished|100|100|100|0|0" >&2
                       echo "SILVER_FILE|$OUT|1080|$EXT"
                       """);

        var prefs = new FilePreferencesService(_prefsPath);
        prefs.SavePreferences(new AppPreferences
        {
            LibraryDirectory = _libraryDir,
            YtDlpExecutablePath = _fakeYtDlpPath,
            MaxConcurrentDownloads = 2
        });

        using var library = new JsonLibraryService(prefs);
        using var coordinator = new DownloadCoordinatorService(prefs, library, _queuePath);

        var libVideo = new VideoSummary("dQw4w9WgXcQ", "Test", "Chan", TimeSpan.FromMinutes(1), "", false);
        var extVideo = new VideoSummary("M7lc1UVf-VE", "Ext", "Chan", TimeSpan.FromMinutes(2), "", false);

        var completedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completedCount = 0;
        coordinator.JobCompleted += (_, _) =>
        {
            if (Interlocked.Increment(ref completedCount) == 2)
                completedTcs.TrySetResult();
        };

        await coordinator.EnqueueAsync(new DownloadEnqueueRequest([libVideo], new DownloadOptions
        {
            DestinationKind = DownloadDestinationKind.Library,
            Container = DownloadContainerFormat.Mp4
        }));

        await coordinator.EnqueueAsync(new DownloadEnqueueRequest([extVideo], new DownloadOptions
        {
            DestinationKind = DownloadDestinationKind.CustomFolder,
            CustomFolderPath = _externalDir,
            Container = DownloadContainerFormat.Mp4
        }));

        await completedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(library.ContainsPlayable("dQw4w9WgXcQ"));
        Assert.False(library.ContainsPlayable("M7lc1UVf-VE"));
        Assert.True(File.Exists(Path.Combine(_externalDir, "Test [M7lc1UVf-VE].mp4")));

        // Re-downloading same Library video in MKV must replace old .mp4 file on disk.
        var replaceTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.JobCompleted += (_, args) =>
        {
            if (args.Job.Video.Id == "dQw4w9WgXcQ" && args.Job.ResolvedFilePath?.EndsWith(".mkv") == true)
                replaceTcs.TrySetResult();
        };

        await coordinator.EnqueueAsync(new DownloadEnqueueRequest([libVideo], new DownloadOptions
        {
            DestinationKind = DownloadDestinationKind.Library,
            Container = DownloadContainerFormat.Mkv
        }));

        await replaceTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(File.Exists(Path.Combine(_libraryDir, "Test [dQw4w9WgXcQ].mp4")));
        Assert.True(File.Exists(Path.Combine(_libraryDir, "Test [dQw4w9WgXcQ].mkv")));
        Assert.Equal("MKV", library.TryGetPlayableEntry("dQw4w9WgXcQ")!.Container);
    }

    [Fact]
    public async Task PauseAndRestart_PreservesPartFilesAndRestoresPausedJob()
    {
        var startedMarker = Path.Combine(_tempRoot, "started");
        WriteFakeYtDlp($$"""
                         TARGET_DIR=""
                         URL=""
                         while [[ $# -gt 0 ]]; do
                           case "$1" in
                             -P) TARGET_DIR="$2"; shift 2 ;;
                             *) URL="$1"; shift ;;
                           esac
                         done
                         VID="${URL##*v=}"
                         PART="$TARGET_DIR/Test [$VID].mp4.part"
                         printf 'partial' > "$PART"
                         touch "{{startedMarker}}"
                         echo "SILVER_DL|downloading|7|100|100|100|5"
                         sleep 30
                         """);

        var prefs = new FilePreferencesService(_prefsPath);
        prefs.SavePreferences(new AppPreferences
        {
            LibraryDirectory = _libraryDir,
            YtDlpExecutablePath = _fakeYtDlpPath,
            MaxConcurrentDownloads = 1
        });

        using var library = new JsonLibraryService(prefs);
        var coordinator = new DownloadCoordinatorService(prefs, library, _queuePath);

        var video = new VideoSummary("abc123_X-yZ", "Test", "Chan", TimeSpan.FromMinutes(1), "", false);
        await coordinator.EnqueueAsync(new DownloadEnqueueRequest([video], new DownloadOptions()));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(startedMarker) && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.True(File.Exists(startedMarker));

        var jobId = Assert.Single(coordinator.Jobs).Id;
        await coordinator.PauseAsync(jobId);

        deadline = DateTime.UtcNow.AddSeconds(5);
        while (coordinator.Jobs[0].Status != DownloadJobStatus.Paused && DateTime.UtcNow < deadline)
            await Task.Delay(50);

        Assert.Equal(DownloadJobStatus.Paused, coordinator.Jobs[0].Status);
        Assert.True(File.Exists(Path.Combine(_libraryDir, "Test [abc123_X-yZ].mp4.part")));

        coordinator.Dispose();

        using var restoredCoordinator = new DownloadCoordinatorService(prefs, library, _queuePath);
        var restoredJob = Assert.Single(restoredCoordinator.Jobs);
        Assert.Equal(DownloadJobStatus.Paused, restoredJob.Status);

        await restoredCoordinator.CancelAsync(restoredJob.Id);
        Assert.Equal(DownloadJobStatus.Canceled, restoredCoordinator.Jobs[0].Status);
        Assert.False(File.Exists(Path.Combine(_libraryDir, "Test [abc123_X-yZ].mp4.part")));
    }

    [Fact]
    public async Task EnqueueAsync_NonYouTubeId_IsSkipped()
    {
        var prefs = new FilePreferencesService(_prefsPath);
        using var library = new JsonLibraryService(prefs);
        using var coordinator = new DownloadCoordinatorService(prefs, library, _queuePath);

        var badVideo = new VideoSummary("not-a-youtube-id", "Bad", "Chan", TimeSpan.FromMinutes(1), "", false);
        var result = await coordinator.EnqueueAsync(new DownloadEnqueueRequest([badVideo], new DownloadOptions()));

        Assert.Equal(0, result.EnqueuedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Empty(coordinator.Jobs);
    }
}