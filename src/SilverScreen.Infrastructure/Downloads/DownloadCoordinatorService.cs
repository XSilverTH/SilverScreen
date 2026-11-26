using System.Diagnostics;
using System.Text.Json;
using Serilog;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Downloads;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Infrastructure.Downloads;

public sealed class DownloadJobEventArgs(DownloadJob job) : EventArgs
{
    public DownloadJob Job { get; } = job;
}

/// <summary>
///     App-managed yt-dlp download worker pool with bounded concurrency, per-job progress/speed/ETA,
///     pause/resume via <c>--continue</c>, cancel/retry, and persisted queue recovery across restarts.
/// </summary>
public sealed class DownloadCoordinatorService : IDownloadService, IDisposable
{
    private const int CurrentQueueSchemaVersion = 1;
    private static readonly ILogger Logger = Log.ForContext<DownloadCoordinatorService>();
    private static readonly TimeSpan KillGracePeriod = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProgressNotifyInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan QueuePersistInterval = TimeSpan.FromSeconds(2);

    private readonly Dictionary<Guid, CancellationTokenSource> _activeCts = [];
    private readonly Dictionary<Guid, Task> _activeTasks = [];
    private readonly HashSet<Guid> _cancelRequested = [];
    private readonly ICookieFileProvider? _cookieFiles;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly List<DownloadJob> _jobs = [];
    private readonly ILibraryService _libraryService;
    private readonly Lock _lock = new();
    private readonly HashSet<Guid> _pauseRequested = [];
    private readonly IPreferencesService _preferencesService;
    private readonly string _queueFilePath;
    private readonly ISponsorBlockService? _sponsorBlockService;
    private readonly IThumbnailService? _thumbnailService;
    private bool _disposed;
    private long _lastProgressNotifyTick;
    private long _lastPersistTick;

    public DownloadCoordinatorService(
        IPreferencesService preferencesService,
        ILibraryService libraryService,
        ICookieFileProvider? cookieFiles = null,
        IThumbnailService? thumbnailService = null,
        ISponsorBlockService? sponsorBlockService = null)
        : this(
            preferencesService,
            libraryService,
            GetDefaultQueueFilePath(),
            cookieFiles,
            thumbnailService,
            sponsorBlockService)
    {
    }

    internal DownloadCoordinatorService(
        IPreferencesService preferencesService,
        ILibraryService libraryService,
        string queueFilePath,
        ICookieFileProvider? cookieFiles = null,
        IThumbnailService? thumbnailService = null,
        ISponsorBlockService? sponsorBlockService = null)
    {
        _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _queueFilePath = string.IsNullOrWhiteSpace(queueFilePath)
            ? throw new ArgumentException("Queue file path is required.", nameof(queueFilePath))
            : queueFilePath;
        _cookieFiles = cookieFiles;
        _thumbnailService = thumbnailService;
        _sponsorBlockService = sponsorBlockService;

        LoadQueueFromDisk();
        _preferencesService.PreferencesChanged += OnPreferencesChanged;
    }

    public IReadOnlyList<DownloadJob> Jobs
    {
        get
        {
            lock (_lock)
            {
                return [.. _jobs];
            }
        }
    }

    public event EventHandler? JobsChanged;
    public event EventHandler<DownloadJobEventArgs>? JobCompleted;
    public event EventHandler<DownloadJobEventArgs>? JobFailed;

    public void Dispose()
    {
        Task[] running;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _preferencesService.PreferencesChanged -= OnPreferencesChanged;

            // Convert active/queued jobs to Paused before killing processes so they can resume on next launch.
            for (var i = 0; i < _jobs.Count; i++)
            {
                var job = _jobs[i];
                if (job.IsActive)
                {
                    _pauseRequested.Add(job.Id);
                    _jobs[i] = job with
                    {
                        Status = DownloadJobStatus.Paused,
                        SpeedBytesPerSecond = null,
                        Eta = null,
                        StatusText = "Paused"
                    };
                }
            }

            PersistQueueLocked(force: true);
            _disposeCts.Cancel();
            running = [.. _activeTasks.Values];
        }

        try
        {
            Task.WhenAll(running).Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception)
        {
        }

        _disposeCts.Dispose();
    }

    public Task<DownloadEnqueueResult> EnqueueAsync(
        DownloadEnqueueRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Videos.Count == 0)
            return Task.FromResult(new DownloadEnqueueResult(0, 0, "No videos selected to download."));

        var prefs = _preferencesService.GetPreferences();
        var options = request.Options.Normalized();
        var targetDirectory = options.ResolveTargetDirectory(prefs.LibraryDirectory);
        var enqueued = 0;
        var skipped = 0;

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            foreach (var video in request.Videos)
            {
                if (string.IsNullOrWhiteSpace(video.Id) || !PlaybackRequest.LooksLikeYouTubeVideoId(video.Id))
                {
                    skipped++;
                    continue;
                }

                // Avoid queueing duplicate active/paused jobs for the exact same video & destination.
                var existingIndex = _jobs.FindIndex(j =>
                    string.Equals(j.Video.Id, video.Id, StringComparison.Ordinal) &&
                    string.Equals(j.TargetDirectory, targetDirectory, StringComparison.Ordinal) &&
                    (j.IsActive || j.Status == DownloadJobStatus.Paused));
                if (existingIndex >= 0)
                {
                    skipped++;
                    continue;
                }

                var job = new DownloadJob(
                    Guid.NewGuid(),
                    video,
                    options,
                    targetDirectory,
                    request.PresetName,
                    DownloadJobStatus.Queued,
                    0,
                    null,
                    null,
                    null,
                    null,
                    "Queued",
                    null,
                    null,
                    DateTimeOffset.UtcNow);
                _jobs.Insert(0, job);
                enqueued++;
            }

            if (enqueued > 0)
                PersistQueueLocked(force: true);
        }

        if (enqueued > 0)
        {
            ScheduleProcessing();
            JobsChanged?.Invoke(this, EventArgs.Empty);
        }

        var message = (enqueued, skipped) switch
        {
            (0, > 0) => "Selected video(s) cannot be downloaded or are already queued.",
            (1, 0) => $"Queued “{request.Videos[0].Title}” for download.",
            ( > 1, 0) => $"Queued {enqueued} videos for download.",
            _ => $"Queued {enqueued} videos ({skipped} skipped)."
        };

        return Task.FromResult(new DownloadEnqueueResult(enqueued, skipped, message));
    }

    public Task PauseAsync(Guid jobId)
    {
        var changed = false;
        lock (_lock)
        {
            if (_disposed) return Task.CompletedTask;
            var index = FindJobIndexLocked(jobId);
            if (index < 0) return Task.CompletedTask;

            var job = _jobs[index];
            if (!job.CanPause) return Task.CompletedTask;

            if (job.Status == DownloadJobStatus.Queued)
            {
                _jobs[index] = job with
                {
                    Status = DownloadJobStatus.Paused,
                    SpeedBytesPerSecond = null,
                    Eta = null,
                    StatusText = "Paused"
                };
                PersistQueueLocked(force: true);
                changed = true;
            }
            else if (_activeCts.TryGetValue(jobId, out var cts))
            {
                _pauseRequested.Add(jobId);
                cts.Cancel();
            }
        }

        if (changed)
            JobsChanged?.Invoke(this, EventArgs.Empty);

        return Task.CompletedTask;
    }

    public Task ResumeAsync(Guid jobId)
    {
        var changed = false;
        lock (_lock)
        {
            if (_disposed) return Task.CompletedTask;
            var index = FindJobIndexLocked(jobId);
            if (index < 0) return Task.CompletedTask;

            var job = _jobs[index];
            if (!job.CanResume) return Task.CompletedTask;

            _pauseRequested.Remove(jobId);
            _cancelRequested.Remove(jobId);
            _jobs[index] = job with
            {
                Status = DownloadJobStatus.Queued,
                ErrorMessage = null,
                StatusText = "Queued"
            };
            PersistQueueLocked(force: true);
            changed = true;
        }

        if (changed)
        {
            ScheduleProcessing();
            JobsChanged?.Invoke(this, EventArgs.Empty);
        }

        return Task.CompletedTask;
    }

    public Task CancelAsync(Guid jobId)
    {
        var changed = false;
        string? cleanupDir = null;
        string? cleanupVideoId = null;

        lock (_lock)
        {
            if (_disposed) return Task.CompletedTask;
            var index = FindJobIndexLocked(jobId);
            if (index < 0) return Task.CompletedTask;

            var job = _jobs[index];
            if (!job.CanCancel) return Task.CompletedTask;

            if (job.Status is DownloadJobStatus.Queued or DownloadJobStatus.Paused)
            {
                cleanupDir = job.TargetDirectory;
                cleanupVideoId = job.Video.Id;
                _jobs[index] = job with
                {
                    Status = DownloadJobStatus.Canceled,
                    SpeedBytesPerSecond = null,
                    Eta = null,
                    StatusText = "Canceled"
                };
                PersistQueueLocked(force: true);
                changed = true;
            }
            else if (_activeCts.TryGetValue(jobId, out var cts))
            {
                _cancelRequested.Add(jobId);
                cts.Cancel();
            }
        }

        if (cleanupDir is not null && cleanupVideoId is not null)
            CleanupPartFiles(cleanupDir, cleanupVideoId);

        if (changed)
            JobsChanged?.Invoke(this, EventArgs.Empty);

        return Task.CompletedTask;
    }

    public Task RetryAsync(Guid jobId)
    {
        var changed = false;
        lock (_lock)
        {
            if (_disposed) return Task.CompletedTask;
            var index = FindJobIndexLocked(jobId);
            if (index < 0) return Task.CompletedTask;

            var job = _jobs[index];
            if (!job.CanRetry) return Task.CompletedTask;

            _pauseRequested.Remove(jobId);
            _cancelRequested.Remove(jobId);
            _jobs[index] = job with
            {
                Status = DownloadJobStatus.Queued,
                ProgressFraction = 0,
                DownloadedBytes = null,
                TotalBytes = null,
                SpeedBytesPerSecond = null,
                Eta = null,
                ErrorMessage = null,
                StatusText = "Queued",
                CompletedAt = null
            };
            PersistQueueLocked(force: true);
            changed = true;
        }

        if (changed)
        {
            ScheduleProcessing();
            JobsChanged?.Invoke(this, EventArgs.Empty);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid jobId)
    {
        var changed = false;
        lock (_lock)
        {
            if (_disposed) return Task.CompletedTask;
            var index = FindJobIndexLocked(jobId);
            if (index >= 0 && !_jobs[index].IsActive)
            {
                _jobs.RemoveAt(index);
                PersistQueueLocked(force: true);
                changed = true;
            }
        }

        if (changed)
            JobsChanged?.Invoke(this, EventArgs.Empty);

        return Task.CompletedTask;
    }

    public Task ClearFinishedAsync()
    {
        var removed = 0;
        lock (_lock)
        {
            if (_disposed) return Task.CompletedTask;
            removed = _jobs.RemoveAll(static j => j.IsFinished);
            if (removed > 0)
                PersistQueueLocked(force: true);
        }

        if (removed > 0)
            JobsChanged?.Invoke(this, EventArgs.Empty);

        return Task.CompletedTask;
    }

    private void OnPreferencesChanged(object? sender, AppPreferences preferences)
    {
        ScheduleProcessing();
    }

    private void ScheduleProcessing()
    {
        List<(DownloadJob Job, CancellationToken Token)> toStart = [];
        var maxConcurrency = Math.Clamp(
            _preferencesService.GetPreferences().MaxConcurrentDownloads,
            AppPreferences.MinConcurrentDownloads,
            AppPreferences.MaxConcurrentDownloadsLimit);

        lock (_lock)
        {
            if (_disposed) return;

            while (_activeTasks.Count + toStart.Count < maxConcurrency)
            {
                // Process oldest queued jobs first (queued items are inserted at 0, so scan from the back).
                var nextIndex = -1;
                for (var i = _jobs.Count - 1; i >= 0; i--)
                {
                    if (_jobs[i].Status == DownloadJobStatus.Queued)
                    {
                        nextIndex = i;
                        break;
                    }
                }

                if (nextIndex < 0)
                    break;

                var job = _jobs[nextIndex] with
                {
                    Status = DownloadJobStatus.Downloading,
                    ErrorMessage = null,
                    StatusText = "Starting…"
                };
                _jobs[nextIndex] = job;

                var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
                _activeCts[job.Id] = cts;
                toStart.Add((job, cts.Token));
            }

            if (toStart.Count > 0)
                PersistQueueLocked(force: true);
        }

        if (toStart.Count == 0)
            return;

        foreach (var (job, token) in toStart)
        {
            var task = Task.Run(() => RunJobAsync(job, token), CancellationToken.None);
            lock (_lock)
            {
                if (_disposed) continue;
                _activeTasks[job.Id] = task;
            }
        }

        JobsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task RunJobAsync(DownloadJob initialJob, CancellationToken cancellationToken)
    {
        var jobId = initialJob.Id;
        CookieFileLease? cookieLease = null;
        Task<IReadOnlyList<SponsorBlockSegment>>? sponsorBlockTask = null;

        try
        {
            var prefs = _preferencesService.GetPreferences();
            Directory.CreateDirectory(initialJob.TargetDirectory);
            cookieLease = _cookieFiles?.CreateCookieFile();

            var isLibrary = initialJob.Options.DestinationKind == DownloadDestinationKind.Library;
            var previousEntry = isLibrary ? _libraryService.TryGetEntry(initialJob.Video.Id) : null;
            var hasPartFiles = HasPartFiles(initialJob.TargetDirectory, initialJob.Video.Id);
            var forceOverwrite = isLibrary && previousEntry is not null && !hasPartFiles;

            if (isLibrary && _sponsorBlockService is not null)
                sponsorBlockTask = FetchSponsorBlockSegmentsBestEffortAsync(initialJob.Video.Id, cancellationToken);

            var startInfo = YtDlpDownloadCommandBuilder.Build(
                initialJob,
                prefs.YtDlpExecutablePath,
                cookieLease?.Path,
                [.. prefs.SponsorBlockCategories],
                forceOverwrite);

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
                throw new InvalidOperationException("Failed to start yt-dlp process.");

            string? finalFilePath = null;
            string? targetFilePath = null;
            int? actualHeight = null;
            string? actualExt = null;
            var stderrLines = new Queue<string>(40);

            var stdoutTask = Task.Run(async () =>
            {
                while (await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
                {
                    if (YtDlpDownloadCommandBuilder.TryParseProgressLine(line, out var sample))
                    {
                        ApplyProgressSample(jobId, sample);
                    }
                    else if (YtDlpDownloadCommandBuilder.TryParseFilePrintLine(
                                 line, YtDlpDownloadCommandBuilder.FinalFilePrefix, out var finalPrint))
                    {
                        finalFilePath = finalPrint.FilePath;
                        actualHeight ??= finalPrint.Height;
                        actualExt ??= finalPrint.Extension;
                    }
                    else if (YtDlpDownloadCommandBuilder.TryParseFilePrintLine(
                                 line, YtDlpDownloadCommandBuilder.TargetFilePrefix, out var targetPrint))
                    {
                        targetFilePath = targetPrint.FilePath;
                        actualHeight ??= targetPrint.Height;
                        actualExt ??= targetPrint.Extension;
                    }
                    else if (YtDlpDownloadCommandBuilder.IsPostProcessingLine(line))
                    {
                        MarkPostProcessing(jobId);
                    }
                }
            }, CancellationToken.None);

            var stderrTask = Task.Run(async () =>
            {
                while (await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (YtDlpDownloadCommandBuilder.TryParseProgressLine(line, out var sample))
                    {
                        ApplyProgressSample(jobId, sample);
                        continue;
                    }

                    lock (stderrLines)
                    {
                        if (stderrLines.Count >= 40)
                            stderrLines.Dequeue();
                        stderrLines.Enqueue(line);
                    }

                    if (YtDlpDownloadCommandBuilder.IsPostProcessingLine(line))
                        MarkPostProcessing(jobId);
                }
            }, CancellationToken.None);

            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await KillProcessTreeAsync(process, stdoutTask, stderrTask).ConfigureAwait(false);
                HandleJobCanceled(jobId, initialJob);
                return;
            }

            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                string stderrSummary;
                lock (stderrLines)
                {
                    stderrSummary = stderrLines.Count > 0
                        ? DiagnosticSanitizer.Sanitize(stderrLines.Last())
                        : $"yt-dlp exited with code {process.ExitCode}.";
                }

                FailJob(jobId, stderrSummary);
                return;
            }

            var resolvedPath = ResolveOutputFilePath(
                finalFilePath,
                targetFilePath,
                initialJob.TargetDirectory,
                initialJob.Video.Id);

            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            {
                FailJob(jobId, "Download finished, but the output media file could not be found.");
                return;
            }

            if (isLibrary)
            {
                await FinalizeLibraryEntryAsync(
                    initialJob,
                    resolvedPath,
                    actualHeight,
                    actualExt,
                    previousEntry,
                    sponsorBlockTask).ConfigureAwait(false);
            }

            CompleteJob(jobId, resolvedPath);
        }
        catch (OperationCanceledException)
        {
            HandleJobCanceled(jobId, initialJob);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Download job {JobId} failed", jobId);
            FailJob(jobId, DiagnosticSanitizer.Sanitize(ex.Message));
        }
        finally
        {
            cookieLease?.Dispose();
            lock (_lock)
            {
                if (_activeCts.Remove(jobId, out var cts))
                    cts.Dispose();
                _activeTasks.Remove(jobId);
                _pauseRequested.Remove(jobId);
                _cancelRequested.Remove(jobId);
            }

            ScheduleProcessing();
        }
    }

    private async Task FinalizeLibraryEntryAsync(
        DownloadJob job,
        string resolvedPath,
        int? actualHeight,
        string? actualExt,
        LibraryEntry? previousEntry,
        Task<IReadOnlyList<SponsorBlockSegment>>? sponsorBlockTask)
    {
        var libraryDir = _libraryService.LibraryDirectory;
        var relativeFilePath = Path.GetRelativePath(libraryDir, resolvedPath);

        if (previousEntry is not null)
        {
            var previousAbsPath = previousEntry.ResolveFilePath(libraryDir);
            if (!string.Equals(previousAbsPath, resolvedPath, StringComparison.Ordinal) &&
                File.Exists(previousAbsPath))
            {
                TryDeleteFile(previousAbsPath);
            }
        }

        var relativeThumbPath = await CacheLibraryThumbnailAsync(job.Video, libraryDir).ConfigureAwait(false);
        var fileSize = new FileInfo(resolvedPath).Length;
        var segments = sponsorBlockTask is not null
            ? [.. await sponsorBlockTask.ConfigureAwait(false)]
            : Array.Empty<SponsorBlockSegment>();

        var container = !string.IsNullOrWhiteSpace(actualExt)
            ? actualExt.TrimStart('.').ToUpperInvariant()
            : Path.GetExtension(resolvedPath).TrimStart('.').ToUpperInvariant();

        var entry = new LibraryEntry(
            job.Video with { LocalFilePath = null },
            relativeFilePath,
            relativeThumbPath,
            DateTimeOffset.UtcNow,
            fileSize,
            job.Options.MediaMode,
            job.Options.Quality,
            actualHeight,
            QualityMismatchHelper.FormatQualityLabel(job.Options.MediaMode, job.Options.Quality, actualHeight),
            string.IsNullOrWhiteSpace(container) ? null : container,
            job.Options.SubtitleMode,
            segments);

        await _libraryService.UpsertAsync(entry).ConfigureAwait(false);
    }

    private async Task<string?> CacheLibraryThumbnailAsync(VideoSummary video, string libraryDir)
    {
        if (_thumbnailService is null || string.IsNullOrWhiteSpace(video.ThumbnailUrl))
            return null;

        try
        {
            var result = await _thumbnailService.GetThumbnailAsync(video).ConfigureAwait(false);
            if (result is null || !File.Exists(result.LocalPath))
                return null;

            var thumbsDir = Path.Combine(libraryDir, JsonLibraryService.ThumbnailsDirectoryName);
            Directory.CreateDirectory(thumbsDir);
            var ext = Path.GetExtension(result.LocalPath);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
            var destPath = Path.Combine(thumbsDir, $"{video.Id}{ext}");
            File.Copy(result.LocalPath, destPath, overwrite: true);
            return Path.GetRelativePath(libraryDir, destPath);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to cache offline Library thumbnail for {VideoId}", video.Id);
            return null;
        }
    }

    private async Task<IReadOnlyList<SponsorBlockSegment>> FetchSponsorBlockSegmentsBestEffortAsync(
        string videoId,
        CancellationToken cancellationToken)
    {
        if (_sponsorBlockService is null)
            return [];

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
            return await _sponsorBlockService
                .GetSegmentsAsync(videoId, SponsorBlockCategories.All, timeoutCts.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Skipping SponsorBlock segment prefetch for {VideoId}", videoId);
            return [];
        }
    }

    private void ApplyProgressSample(Guid jobId, YtDlpProgressSample sample)
    {
        var shouldNotify = false;
        lock (_lock)
        {
            if (_disposed) return;
            var index = FindJobIndexLocked(jobId);
            if (index < 0) return;

            var job = _jobs[index];
            var fraction = sample.TotalBytes is > 0 && sample.DownloadedBytes is >= 0
                ? Math.Clamp((double)sample.DownloadedBytes.Value / sample.TotalBytes.Value, 0, 1)
                : job.ProgressFraction;

            var nextStatus = string.Equals(sample.Status, "finished", StringComparison.OrdinalIgnoreCase)
                ? DownloadJobStatus.PostProcessing
                : DownloadJobStatus.Downloading;

            var statusText = nextStatus == DownloadJobStatus.PostProcessing
                ? "Processing…"
                : "Downloading…";

            _jobs[index] = job with
            {
                Status = nextStatus,
                ProgressFraction = fraction,
                DownloadedBytes = sample.DownloadedBytes ?? job.DownloadedBytes,
                TotalBytes = sample.TotalBytes ?? job.TotalBytes,
                SpeedBytesPerSecond = nextStatus == DownloadJobStatus.Downloading ? sample.SpeedBytesPerSecond : null,
                Eta = nextStatus == DownloadJobStatus.Downloading ? sample.Eta : null,
                StatusText = statusText
            };

            var now = Stopwatch.GetTimestamp();
            if (nextStatus != job.Status || Stopwatch.GetElapsedTime(_lastProgressNotifyTick, now) >= ProgressNotifyInterval)
            {
                _lastProgressNotifyTick = now;
                shouldNotify = true;
            }

            if (Stopwatch.GetElapsedTime(_lastPersistTick, now) >= QueuePersistInterval)
            {
                _lastPersistTick = now;
                PersistQueueLocked(force: false);
            }
        }

        if (shouldNotify)
            JobsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MarkPostProcessing(Guid jobId)
    {
        var changed = false;
        lock (_lock)
        {
            if (_disposed) return;
            var index = FindJobIndexLocked(jobId);
            if (index < 0) return;
            var job = _jobs[index];
            if (job.Status == DownloadJobStatus.PostProcessing) return;

            _jobs[index] = job with
            {
                Status = DownloadJobStatus.PostProcessing,
                SpeedBytesPerSecond = null,
                Eta = null,
                StatusText = "Processing…"
            };
            PersistQueueLocked(force: true);
            changed = true;
        }

        if (changed)
            JobsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HandleJobCanceled(Guid jobId, DownloadJob initialJob)
    {
        DownloadJobStatus targetStatus;
        var shouldCleanup = false;
        lock (_lock)
        {
            if (_disposed) return;
            var index = FindJobIndexLocked(jobId);
            if (index < 0) return;

            if (_cancelRequested.Contains(jobId))
            {
                targetStatus = DownloadJobStatus.Canceled;
                shouldCleanup = true;
            }
            else
            {
                // Pause or app shutdown keeps .part files intact.
                targetStatus = DownloadJobStatus.Paused;
            }

            _jobs[index] = _jobs[index] with
            {
                Status = targetStatus,
                SpeedBytesPerSecond = null,
                Eta = null,
                StatusText = targetStatus == DownloadJobStatus.Paused ? "Paused" : "Canceled"
            };
            PersistQueueLocked(force: true);
        }

        if (shouldCleanup)
            CleanupPartFiles(initialJob.TargetDirectory, initialJob.Video.Id);

        JobsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CompleteJob(Guid jobId, string resolvedPath)
    {
        DownloadJob? completed = null;
        lock (_lock)
        {
            if (_disposed) return;
            var index = FindJobIndexLocked(jobId);
            if (index < 0) return;

            completed = _jobs[index] with
            {
                Status = DownloadJobStatus.Completed,
                ProgressFraction = 1.0,
                SpeedBytesPerSecond = null,
                Eta = null,
                StatusText = "Completed",
                ErrorMessage = null,
                ResolvedFilePath = resolvedPath,
                CompletedAt = DateTimeOffset.UtcNow
            };
            _jobs[index] = completed;
            PersistQueueLocked(force: true);
        }

        JobsChanged?.Invoke(this, EventArgs.Empty);
        if (completed is not null)
            JobCompleted?.Invoke(this, new DownloadJobEventArgs(completed));
    }

    private void FailJob(Guid jobId, string errorMessage)
    {
        DownloadJob? failed = null;
        lock (_lock)
        {
            if (_disposed) return;
            var index = FindJobIndexLocked(jobId);
            if (index < 0) return;

            failed = _jobs[index] with
            {
                Status = DownloadJobStatus.Failed,
                SpeedBytesPerSecond = null,
                Eta = null,
                StatusText = "Failed",
                ErrorMessage = errorMessage
            };
            _jobs[index] = failed;
            PersistQueueLocked(force: true);
        }

        JobsChanged?.Invoke(this, EventArgs.Empty);
        if (failed is not null)
            JobFailed?.Invoke(this, new DownloadJobEventArgs(failed));
    }

    private int FindJobIndexLocked(Guid jobId)
    {
        for (var i = 0; i < _jobs.Count; i++)
            if (_jobs[i].Id == jobId)
                return i;
        return -1;
    }

    private void LoadQueueFromDisk()
    {
        try
        {
            if (!File.Exists(_queueFilePath))
                return;

            using var stream = new FileStream(
                _queueFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan);
            var persisted = JsonSerializer.Deserialize(stream, DownloadQueueJsonContext.Default.PersistedDownloadQueue);
            if (persisted?.Jobs is null)
                return;

            lock (_lock)
            {
                foreach (var job in persisted.Jobs)
                {
                    if (job.Status == DownloadJobStatus.Completed)
                        continue;

                    var restored = job.IsActive
                        ? job with
                        {
                            Status = DownloadJobStatus.Paused,
                            SpeedBytesPerSecond = null,
                            Eta = null,
                            StatusText = "Paused"
                        }
                        : job;
                    _jobs.Add(restored);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to restore download queue from {QueueFilePath}", _queueFilePath);
        }
    }

    private void PersistQueueLocked(bool force)
    {
        if (!force && _disposed)
            return;

        try
        {
            var dir = Path.GetDirectoryName(_queueFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var persistable = _jobs
                .Where(static j => j.Status is not DownloadJobStatus.Completed)
                .ToArray();

            var tempPath = Path.Combine(
                dir ?? Directory.GetCurrentDirectory(),
                $".{Path.GetFileName(_queueFilePath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                using (var stream = new FileStream(
                           tempPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           4096,
                           FileOptions.WriteThrough))
                {
                    JsonSerializer.Serialize(
                        stream,
                        new PersistedDownloadQueue(CurrentQueueSchemaVersion, persistable),
                        DownloadQueueJsonContext.Default.PersistedDownloadQueue);
                    stream.Flush(true);
                }

                File.Move(tempPath, _queueFilePath, true);
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to persist download queue to {QueueFilePath}", _queueFilePath);
        }
    }

    private static string? ResolveOutputFilePath(
        string? finalFilePath,
        string? targetFilePath,
        string targetDirectory,
        string videoId)
    {
        if (!string.IsNullOrWhiteSpace(finalFilePath) && File.Exists(finalFilePath))
            return Path.GetFullPath(finalFilePath);

        if (!string.IsNullOrWhiteSpace(targetFilePath) && File.Exists(targetFilePath))
            return Path.GetFullPath(targetFilePath);

        if (!Directory.Exists(targetDirectory))
            return null;

        var idToken = $"[{videoId}]";
        try
        {
            foreach (var file in Directory.EnumerateFiles(targetDirectory))
            {
                var name = Path.GetFileName(file);
                if (!name.Contains(idToken, StringComparison.Ordinal))
                    continue;

                var ext = Path.GetExtension(name);
                if (ext.Equals(".part", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".ytdl", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".srt", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".vtt", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".ass", StringComparison.OrdinalIgnoreCase))
                    continue;

                return Path.GetFullPath(file);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to scan {TargetDirectory} for {VideoId}", targetDirectory, videoId);
        }

        return null;
    }

    private static bool HasPartFiles(string directory, string videoId)
    {
        if (!Directory.Exists(directory))
            return false;

        var idToken = $"[{videoId}]";
        try
        {
            return Directory.EnumerateFiles(directory)
                .Any(file =>
                {
                    var name = Path.GetFileName(file);
                    return name.Contains(idToken, StringComparison.Ordinal) &&
                           (name.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ||
                            name.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase));
                });
        }
        catch
        {
            return false;
        }
    }

    private static void CleanupPartFiles(string directory, string videoId)
    {
        if (!Directory.Exists(directory))
            return;

        var idToken = $"[{videoId}]";
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var name = Path.GetFileName(file);
                if (name.Contains(idToken, StringComparison.Ordinal) &&
                    (name.Contains(".part", StringComparison.OrdinalIgnoreCase) ||
                     name.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase)))
                {
                    TryDeleteFile(file);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to clean up .part files in {Directory}", directory);
        }
    }

    private static async Task KillProcessTreeAsync(Process process, Task stdoutTask, Task stderrTask)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(true);
        }
        catch
        {
        }

        try
        {
            await process.WaitForExitAsync().WaitAsync(KillGracePeriod).ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(KillGracePeriod).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static string GetDefaultQueueFilePath()
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome))
        {
            var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            dataHome = string.IsNullOrWhiteSpace(userHome)
                ? Path.GetTempPath()
                : Path.Combine(userHome, ".local", "share");
        }

        return Path.Combine(dataHome, "SilverScreen", "downloads.json");
    }
}
