using System.Text.Json.Serialization;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Core.Downloads;

/// <summary>
///     Metadata record persisted in the Library's hidden index for a downloaded video.
/// </summary>
public sealed record LibraryEntry(
    VideoSummary Video,
    string RelativeFilePath,
    string? RelativeThumbnailPath,
    DateTimeOffset DownloadedAt,
    long FileSizeBytes,
    [property: JsonConverter(typeof(DownloadMediaModeConverter))]
    DownloadMediaMode MediaMode,
    [property: JsonConverter(typeof(VideoQualityConverter))]
    VideoQuality RequestedQuality,
    int? ActualHeight,
    string QualityLabel,
    string? Container,
    [property: JsonConverter(typeof(DownloadSubtitleModeConverter))]
    DownloadSubtitleMode SubtitleMode,
    SponsorBlockSegment[]? SponsorBlockSegments = null)
{
    public string ResolveFilePath(string libraryDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryDirectory);
        return Path.IsPathRooted(RelativeFilePath)
            ? RelativeFilePath
            : Path.GetFullPath(Path.Combine(libraryDirectory, RelativeFilePath));
    }

    public string? ResolveThumbnailPath(string libraryDirectory)
    {
        if (string.IsNullOrWhiteSpace(RelativeThumbnailPath))
            return null;

        return Path.IsPathRooted(RelativeThumbnailPath)
            ? RelativeThumbnailPath
            : Path.GetFullPath(Path.Combine(libraryDirectory, RelativeThumbnailPath));
    }

    public bool IsFileAvailable(string libraryDirectory)
    {
        if (string.IsNullOrWhiteSpace(libraryDirectory) || string.IsNullOrWhiteSpace(RelativeFilePath))
            return false;

        return File.Exists(ResolveFilePath(libraryDirectory));
    }

    public VideoSummary ToPlayableVideoSummary(string libraryDirectory)
    {
        return Video with
        {
            LocalFilePath = ResolveFilePath(libraryDirectory)
        };
    }
}