using System.Text.Json.Serialization;
using SilverScreen.Core.Domain;

namespace SilverScreen.Core.Downloads;

/// <summary>
///     Immutable snapshot of a single per-video download job tracked by the worker pool.
/// </summary>
public sealed record DownloadJob(
    Guid Id,
    VideoSummary Video,
    DownloadOptions Options,
    string TargetDirectory,
    string? PresetName,
    [property: JsonConverter(typeof(DownloadJobStatusConverter))]
    DownloadJobStatus Status,
    double ProgressFraction,
    long? DownloadedBytes,
    long? TotalBytes,
    double? SpeedBytesPerSecond,
    TimeSpan? Eta,
    string? StatusText,
    string? ErrorMessage,
    string? ResolvedFilePath,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt = null)
{
    [JsonIgnore]
    public bool IsActive => Status is DownloadJobStatus.Queued or DownloadJobStatus.Downloading or DownloadJobStatus.PostProcessing;

    [JsonIgnore]
    public bool CanPause => Status is DownloadJobStatus.Queued or DownloadJobStatus.Downloading;

    [JsonIgnore]
    public bool CanResume => Status == DownloadJobStatus.Paused;

    [JsonIgnore]
    public bool CanCancel => Status is DownloadJobStatus.Queued or DownloadJobStatus.Downloading or DownloadJobStatus.PostProcessing or DownloadJobStatus.Paused;

    [JsonIgnore]
    public bool CanRetry => Status is DownloadJobStatus.Failed or DownloadJobStatus.Canceled;

    [JsonIgnore]
    public bool IsFinished => Status is DownloadJobStatus.Completed or DownloadJobStatus.Failed or DownloadJobStatus.Canceled;
}

public sealed record DownloadEnqueueRequest(
    IReadOnlyList<VideoSummary> Videos,
    DownloadOptions Options,
    string? PresetName = null);

public sealed record DownloadEnqueueResult(
    int EnqueuedCount,
    int SkippedCount,
    string SummaryMessage);
