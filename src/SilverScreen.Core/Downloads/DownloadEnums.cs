using System.Text.Json;
using System.Text.Json.Serialization;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Core.Downloads;

/// <summary>
///     Target storage destination for a download job or preset.
/// </summary>
public enum DownloadDestinationKind
{
    Library = 0,
    CustomFolder = 1
}

/// <summary>
///     Whether a download produces a video+audio file or extracts audio only.
/// </summary>
public enum DownloadMediaMode
{
    VideoAndAudio = 0,
    AudioOnly = 1
}

/// <summary>
///     Target container / audio output format for yt-dlp downloads.
/// </summary>
public enum DownloadContainerFormat
{
    Auto = 0,
    Mp4 = 1,
    Mkv = 2,
    Webm = 3,
    Mp3 = 4,
    M4a = 5,
    Opus = 6,
    Flac = 7
}

/// <summary>
///     How subtitles are handled during download.
/// </summary>
public enum DownloadSubtitleMode
{
    None = 0,
    Embedded = 1,
    Sidecar = 2
}

/// <summary>
///     SponsorBlock processing mode for external folder downloads.
///     (Library downloads store segments in SilverScreen's index instead.)
/// </summary>
public enum SponsorBlockDownloadMode
{
    MarkAsChapters = 0,
    Strip = 1,
    Off = 2
}

/// <summary>
///     Lifecycle state of an individual per-video download job.
/// </summary>
public enum DownloadJobStatus
{
    Queued = 0,
    Downloading = 1,
    PostProcessing = 2,
    Paused = 3,
    Completed = 4,
    Failed = 5,
    Canceled = 6
}

public static class DownloadDestinationKinds
{
    public static string ToPersistedString(this DownloadDestinationKind kind)
    {
        return kind switch
        {
            DownloadDestinationKind.CustomFolder => "CustomFolder",
            _ => "Library"
        };
    }

    public static string ToDisplayString(this DownloadDestinationKind kind)
    {
        return kind switch
        {
            DownloadDestinationKind.CustomFolder => "Choose folder…",
            _ => "Library"
        };
    }

    public static DownloadDestinationKind Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DownloadDestinationKind.Library;

        var trimmed = value.AsSpan().Trim();
        return trimmed.Equals("CustomFolder", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("External", StringComparison.OrdinalIgnoreCase)
            ? DownloadDestinationKind.CustomFolder
            : DownloadDestinationKind.Library;
    }
}

public static class DownloadMediaModes
{
    public static string ToPersistedString(this DownloadMediaMode mode)
    {
        return mode switch
        {
            DownloadMediaMode.AudioOnly => "AudioOnly",
            _ => "VideoAndAudio"
        };
    }

    public static string ToDisplayString(this DownloadMediaMode mode)
    {
        return mode switch
        {
            DownloadMediaMode.AudioOnly => "Audio only",
            _ => "Video + Audio"
        };
    }

    public static DownloadMediaMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DownloadMediaMode.VideoAndAudio;

        return value.AsSpan().Trim().Equals("AudioOnly", StringComparison.OrdinalIgnoreCase)
            ? DownloadMediaMode.AudioOnly
            : DownloadMediaMode.VideoAndAudio;
    }
}

public static class DownloadContainerFormats
{
    public static readonly DownloadContainerFormat[] VideoContainers =
    [
        DownloadContainerFormat.Auto,
        DownloadContainerFormat.Mp4,
        DownloadContainerFormat.Mkv,
        DownloadContainerFormat.Webm
    ];

    public static readonly DownloadContainerFormat[] AudioContainers =
    [
        DownloadContainerFormat.Auto,
        DownloadContainerFormat.M4a,
        DownloadContainerFormat.Opus,
        DownloadContainerFormat.Mp3,
        DownloadContainerFormat.Flac
    ];

    public static IReadOnlyList<DownloadContainerFormat> GetForMode(DownloadMediaMode mode)
    {
        return mode == DownloadMediaMode.AudioOnly ? AudioContainers : VideoContainers;
    }

    public static bool IsAudioContainer(this DownloadContainerFormat format)
    {
        return format is DownloadContainerFormat.Mp3
            or DownloadContainerFormat.M4a
            or DownloadContainerFormat.Opus
            or DownloadContainerFormat.Flac;
    }

    public static DownloadContainerFormat NormalizeForMode(this DownloadContainerFormat format, DownloadMediaMode mode)
    {
        if (format == DownloadContainerFormat.Auto)
            return DownloadContainerFormat.Auto;

        return mode == DownloadMediaMode.AudioOnly
            ? format.IsAudioContainer() ? format : DownloadContainerFormat.Auto
            : !format.IsAudioContainer()
                ? format
                : DownloadContainerFormat.Auto;
    }

    public static string ToPersistedString(this DownloadContainerFormat format)
    {
        return format switch
        {
            DownloadContainerFormat.Mp4 => "mp4",
            DownloadContainerFormat.Mkv => "mkv",
            DownloadContainerFormat.Webm => "webm",
            DownloadContainerFormat.Mp3 => "mp3",
            DownloadContainerFormat.M4a => "m4a",
            DownloadContainerFormat.Opus => "opus",
            DownloadContainerFormat.Flac => "flac",
            _ => "Auto"
        };
    }

    public static string ToDisplayString(this DownloadContainerFormat format)
    {
        return format switch
        {
            DownloadContainerFormat.Mp4 => "MP4",
            DownloadContainerFormat.Mkv => "MKV",
            DownloadContainerFormat.Webm => "WebM",
            DownloadContainerFormat.Mp3 => "MP3",
            DownloadContainerFormat.M4a => "M4A",
            DownloadContainerFormat.Opus => "Opus",
            DownloadContainerFormat.Flac => "FLAC",
            _ => "Auto"
        };
    }

    public static DownloadContainerFormat Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DownloadContainerFormat.Auto;

        var trimmed = value.AsSpan().Trim();
        if (trimmed.Equals("mp4", StringComparison.OrdinalIgnoreCase)) return DownloadContainerFormat.Mp4;
        if (trimmed.Equals("mkv", StringComparison.OrdinalIgnoreCase)) return DownloadContainerFormat.Mkv;
        if (trimmed.Equals("webm", StringComparison.OrdinalIgnoreCase)) return DownloadContainerFormat.Webm;
        if (trimmed.Equals("mp3", StringComparison.OrdinalIgnoreCase)) return DownloadContainerFormat.Mp3;
        if (trimmed.Equals("m4a", StringComparison.OrdinalIgnoreCase)) return DownloadContainerFormat.M4a;
        if (trimmed.Equals("opus", StringComparison.OrdinalIgnoreCase)) return DownloadContainerFormat.Opus;
        if (trimmed.Equals("flac", StringComparison.OrdinalIgnoreCase)) return DownloadContainerFormat.Flac;
        return DownloadContainerFormat.Auto;
    }
}

public static class DownloadSubtitleModes
{
    public static string ToPersistedString(this DownloadSubtitleMode mode)
    {
        return mode switch
        {
            DownloadSubtitleMode.Embedded => "Embedded",
            DownloadSubtitleMode.Sidecar => "Sidecar",
            _ => "None"
        };
    }

    public static string ToDisplayString(this DownloadSubtitleMode mode)
    {
        return mode switch
        {
            DownloadSubtitleMode.Embedded => "Embedded",
            DownloadSubtitleMode.Sidecar => "Sidecar (.srt)",
            _ => "Off"
        };
    }

    public static DownloadSubtitleMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DownloadSubtitleMode.None;

        var trimmed = value.AsSpan().Trim();
        if (trimmed.Equals("Embedded", StringComparison.OrdinalIgnoreCase)) return DownloadSubtitleMode.Embedded;
        if (trimmed.Equals("Sidecar", StringComparison.OrdinalIgnoreCase)) return DownloadSubtitleMode.Sidecar;
        return DownloadSubtitleMode.None;
    }
}

public static class SponsorBlockDownloadModes
{
    public static string ToPersistedString(this SponsorBlockDownloadMode mode)
    {
        return mode switch
        {
            SponsorBlockDownloadMode.Strip => "Strip",
            SponsorBlockDownloadMode.Off => "Off",
            _ => "MarkAsChapters"
        };
    }

    public static string ToDisplayString(this SponsorBlockDownloadMode mode)
    {
        return mode switch
        {
            SponsorBlockDownloadMode.Strip => "Strip segments",
            SponsorBlockDownloadMode.Off => "Off",
            _ => "Mark as chapters"
        };
    }

    public static SponsorBlockDownloadMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return SponsorBlockDownloadMode.MarkAsChapters;

        var trimmed = value.AsSpan().Trim();
        if (trimmed.Equals("Strip", StringComparison.OrdinalIgnoreCase)) return SponsorBlockDownloadMode.Strip;
        if (trimmed.Equals("Off", StringComparison.OrdinalIgnoreCase)) return SponsorBlockDownloadMode.Off;
        return SponsorBlockDownloadMode.MarkAsChapters;
    }
}

public static class DownloadJobStatuses
{
    public static string ToPersistedString(this DownloadJobStatus status)
    {
        return status switch
        {
            DownloadJobStatus.Downloading => "Downloading",
            DownloadJobStatus.PostProcessing => "PostProcessing",
            DownloadJobStatus.Paused => "Paused",
            DownloadJobStatus.Completed => "Completed",
            DownloadJobStatus.Failed => "Failed",
            DownloadJobStatus.Canceled => "Canceled",
            _ => "Queued"
        };
    }

    public static DownloadJobStatus Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DownloadJobStatus.Queued;

        var trimmed = value.AsSpan().Trim();
        if (trimmed.Equals("Downloading", StringComparison.OrdinalIgnoreCase)) return DownloadJobStatus.Downloading;
        if (trimmed.Equals("PostProcessing", StringComparison.OrdinalIgnoreCase))
            return DownloadJobStatus.PostProcessing;
        if (trimmed.Equals("Paused", StringComparison.OrdinalIgnoreCase)) return DownloadJobStatus.Paused;
        if (trimmed.Equals("Completed", StringComparison.OrdinalIgnoreCase)) return DownloadJobStatus.Completed;
        if (trimmed.Equals("Failed", StringComparison.OrdinalIgnoreCase)) return DownloadJobStatus.Failed;
        if (trimmed.Equals("Canceled", StringComparison.OrdinalIgnoreCase)) return DownloadJobStatus.Canceled;
        return DownloadJobStatus.Queued;
    }
}

public static class QualityMismatchHelper
{
    public static int? GetTargetHeight(VideoQuality quality)
    {
        return quality switch
        {
            VideoQuality.P4320 => 4320,
            VideoQuality.P2160 => 2160,
            VideoQuality.P1440 => 1440,
            VideoQuality.P1080 => 1080,
            VideoQuality.P720 => 720,
            VideoQuality.P480 => 480,
            VideoQuality.P360 => 360,
            VideoQuality.P240 => 240,
            VideoQuality.P144 => 144,
            _ => null
        };
    }

    public static string FormatQualityLabel(DownloadMediaMode mediaMode, VideoQuality requestedQuality,
        int? actualHeight)
    {
        if (mediaMode == DownloadMediaMode.AudioOnly)
            return "Audio";

        if (actualHeight is > 0)
            return $"{actualHeight.Value}p";

        return requestedQuality.ToPersistedString();
    }

    /// <summary>
    ///     Determines whether playing the local Library copy differs from the user's configured
    ///     default quality preference, returning an informational toast message if mismatched.
    /// </summary>
    public static bool TryGetMismatchMessage(
        LibraryEntry entry,
        VideoQuality configuredQuality,
        out string message)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.MediaMode == DownloadMediaMode.AudioOnly)
        {
            message = "Playing offline audio copy";
            return true;
        }

        if (configuredQuality == VideoQuality.Best)
        {
            if (entry.RequestedQuality == VideoQuality.Best)
            {
                message = string.Empty;
                return false;
            }

            message = $"Playing offline copy ({entry.QualityLabel})";
            return true;
        }

        var targetHeight = GetTargetHeight(configuredQuality);
        if (targetHeight.HasValue && entry.ActualHeight is > 0)
        {
            if (entry.ActualHeight.Value == targetHeight.Value)
            {
                message = string.Empty;
                return false;
            }

            message = $"Playing offline copy ({entry.QualityLabel})";
            return true;
        }

        if (entry.RequestedQuality == configuredQuality)
        {
            message = string.Empty;
            return false;
        }

        message = $"Playing offline copy ({entry.QualityLabel})";
        return true;
    }
}

public sealed class DownloadDestinationKindConverter : JsonConverter<DownloadDestinationKind>
{
    public override DownloadDestinationKind Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.String
            ? DownloadDestinationKinds.Parse(reader.GetString())
            : DownloadDestinationKind.Library;
    }

    public override void Write(Utf8JsonWriter writer, DownloadDestinationKind value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToPersistedString());
    }
}

public sealed class DownloadMediaModeConverter : JsonConverter<DownloadMediaMode>
{
    public override DownloadMediaMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.String
            ? DownloadMediaModes.Parse(reader.GetString())
            : DownloadMediaMode.VideoAndAudio;
    }

    public override void Write(Utf8JsonWriter writer, DownloadMediaMode value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToPersistedString());
    }
}

public sealed class DownloadContainerFormatConverter : JsonConverter<DownloadContainerFormat>
{
    public override DownloadContainerFormat Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.String
            ? DownloadContainerFormats.Parse(reader.GetString())
            : DownloadContainerFormat.Auto;
    }

    public override void Write(Utf8JsonWriter writer, DownloadContainerFormat value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToPersistedString());
    }
}

public sealed class DownloadSubtitleModeConverter : JsonConverter<DownloadSubtitleMode>
{
    public override DownloadSubtitleMode Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.String
            ? DownloadSubtitleModes.Parse(reader.GetString())
            : DownloadSubtitleMode.None;
    }

    public override void Write(Utf8JsonWriter writer, DownloadSubtitleMode value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToPersistedString());
    }
}

public sealed class SponsorBlockDownloadModeConverter : JsonConverter<SponsorBlockDownloadMode>
{
    public override SponsorBlockDownloadMode Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.String
            ? SponsorBlockDownloadModes.Parse(reader.GetString())
            : SponsorBlockDownloadMode.MarkAsChapters;
    }

    public override void Write(Utf8JsonWriter writer, SponsorBlockDownloadMode value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToPersistedString());
    }
}

public sealed class DownloadJobStatusConverter : JsonConverter<DownloadJobStatus>
{
    public override DownloadJobStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.String
            ? DownloadJobStatuses.Parse(reader.GetString())
            : DownloadJobStatus.Queued;
    }

    public override void Write(Utf8JsonWriter writer, DownloadJobStatus value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToPersistedString());
    }
}