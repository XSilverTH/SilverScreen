namespace SilverScreen.Core.Downloads;

/// <summary>
///     Manages the app-wide yt-dlp download worker pool and persisted queue.
/// </summary>
public interface IDownloadService
{
    IReadOnlyList<DownloadJob> Jobs { get; }

    event EventHandler? JobsChanged;

    Task<DownloadEnqueueResult> EnqueueAsync(DownloadEnqueueRequest request,
        CancellationToken cancellationToken = default);

    Task PauseAsync(Guid jobId);

    Task ResumeAsync(Guid jobId);

    Task CancelAsync(Guid jobId);

    Task RetryAsync(Guid jobId);

    Task RemoveAsync(Guid jobId);

    Task ClearFinishedAsync();
}