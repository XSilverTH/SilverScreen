using System.Diagnostics;
using System.Globalization;
using SilverScreen.Core.Downloads;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Infrastructure.Downloads;

public readonly record struct YtDlpProgressSample(
    string Status,
    long? DownloadedBytes,
    long? TotalBytes,
    double? SpeedBytesPerSecond,
    TimeSpan? Eta);

public readonly record struct YtDlpFilePrintResult(
    string FilePath,
    int? Height,
    string? Extension);

/// <summary>
///     Builds <c>yt-dlp</c> download commands and parses structured progress/output lines.
/// </summary>
public static class YtDlpDownloadCommandBuilder
{
    public const string ProgressPrefix = "SILVER_DL|";
    public const string FinalFilePrefix = "SILVER_FILE|";
    public const string TargetFilePrefix = "SILVER_TARGET|";
    private const string OutputTemplate = "%(title).120B [%(id)s].%(ext)s";

    public static ProcessStartInfo Build(
        DownloadJob job,
        string ytDlpExecutablePath,
        string? cookieFilePath = null,
        IReadOnlyList<string>? sponsorBlockCategories = null,
        bool forceOverwrite = false)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (string.IsNullOrWhiteSpace(ytDlpExecutablePath))
            throw new ArgumentException("yt-dlp executable path is required.", nameof(ytDlpExecutablePath));

        var watchUrl = PlaybackRequest.BuildWatchUrl(job.Video.Id)
                       ?? throw new ArgumentException("A valid YouTube video ID is required.", nameof(job));

        var options = job.Options.Normalized();
        var startInfo = new ProcessStartInfo
        {
            FileName = ytDlpExecutablePath.Trim(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var args = startInfo.ArgumentList;
        args.Add("--ignore-config");
        args.Add("--newline");
        args.Add("--progress");
        args.Add("--no-playlist");
        args.Add("--socket-timeout");
        args.Add("15");
        args.Add("--windows-filenames");
        args.Add(forceOverwrite ? "--force-overwrites" : "--continue");
        if (forceOverwrite)
            args.Add("--no-continue");

        args.Add("-P");
        args.Add(job.TargetDirectory);
        args.Add("-o");
        args.Add(OutputTemplate);

        if (!string.IsNullOrWhiteSpace(cookieFilePath))
        {
            args.Add("--cookies");
            args.Add(cookieFilePath);
        }

        if (options.MediaMode == DownloadMediaMode.AudioOnly)
        {
            args.Add("-f");
            args.Add("bestaudio/best");
            args.Add("-x");
            if (options.Container != DownloadContainerFormat.Auto && options.Container.IsAudioContainer())
            {
                args.Add("--audio-format");
                args.Add(options.Container.ToPersistedString());
            }
        }
        else
        {
            var format = YtDlpFormatSelector.ToMpvFormat(options.Quality.ToPersistedString(), options.PreferredCodec)
                         ?? "bestvideo+bestaudio/best";
            args.Add("-f");
            args.Add(format);

            if (options.Container != DownloadContainerFormat.Auto && !options.Container.IsAudioContainer())
            {
                args.Add("--merge-output-format");
                args.Add(options.Container.ToPersistedString());
            }
        }

        if (options.SubtitleMode != DownloadSubtitleMode.None)
        {
            var subLangs = string.IsNullOrWhiteSpace(options.SubtitleLanguage)
                ? "en.*,en,-live_chat"
                : $"{options.SubtitleLanguage.Trim()},-live_chat";
            args.Add("--write-subs");
            args.Add("--write-auto-subs");
            args.Add("--sub-langs");
            args.Add(subLangs);

            if (options.SubtitleMode == DownloadSubtitleMode.Embedded)
            {
                args.Add("--embed-subs");
            }
            else
            {
                args.Add("--convert-subs");
                args.Add("srt");
            }
        }

        if (options.EmbedMetadata)
            args.Add("--embed-metadata");

        if (options.EmbedThumbnail &&
            !(options.MediaMode == DownloadMediaMode.VideoAndAudio &&
              options.Container == DownloadContainerFormat.Webm))
            args.Add("--embed-thumbnail");

        // SponsorBlock flags apply only to external folder downloads; Library downloads keep
        // segments in SilverScreen's local index so the built-in player handles them natively.
        if (options.DestinationKind == DownloadDestinationKind.CustomFolder &&
            options.SponsorBlockMode != SponsorBlockDownloadMode.Off)
        {
            var cats = sponsorBlockCategories is { Count: > 0 }
                ? string.Join(',', sponsorBlockCategories.Where(static c => !string.IsNullOrWhiteSpace(c)))
                : "all";
            if (string.IsNullOrWhiteSpace(cats))
                cats = "all";

            args.Add(options.SponsorBlockMode == SponsorBlockDownloadMode.Strip
                ? "--sponsorblock-remove"
                : "--sponsorblock-mark");
            args.Add(cats);
        }

        args.Add("--progress-template");
        args.Add(
            $"download:{ProgressPrefix}%(progress.status)s|%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s|%(progress.speed)s|%(progress.eta)s");
        args.Add("--print");
        args.Add($"after_move:{FinalFilePrefix}%(filepath)s|%(height)s|%(ext)s");
        args.Add("--print");
        args.Add($"before_dl:{TargetFilePrefix}%(filename)s|%(height)s|%(ext)s");

        args.Add(watchUrl);
        return startInfo;
    }

    public static bool TryParseProgressLine(string? line, out YtDlpProgressSample sample)
    {
        sample = default;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var trimmed = line.Trim();
        if (!trimmed.StartsWith(ProgressPrefix, StringComparison.Ordinal))
            return false;

        var parts = trimmed[ProgressPrefix.Length..].Split('|');
        if (parts.Length < 6)
            return false;

        var status = parts[0].Trim();
        var downloaded = ParseNullableLong(parts[1]);
        var total = ParseNullableLong(parts[2]) ?? ParseNullableLong(parts[3]);
        var speed = ParseNullableDouble(parts[4]);
        var etaSeconds = ParseNullableDouble(parts[5]);
        TimeSpan? eta = etaSeconds is >= 0 and <= 86400 * 30
            ? TimeSpan.FromSeconds(etaSeconds.Value)
            : null;

        sample = new YtDlpProgressSample(status, downloaded, total, speed, eta);
        return true;
    }

    public static bool TryParseFilePrintLine(string? line, string prefix, out YtDlpFilePrintResult result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var trimmed = line.Trim();
        if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var parts = trimmed[prefix.Length..].Split('|');
        if (parts.Length < 1 || string.IsNullOrWhiteSpace(parts[0]) ||
            string.Equals(parts[0], "NA", StringComparison.OrdinalIgnoreCase))
            return false;

        var path = parts[0].Trim();
        var height = parts.Length >= 2 ? ParseNullableInt(parts[1]) : null;
        var ext = parts.Length >= 3 && !string.Equals(parts[2], "NA", StringComparison.OrdinalIgnoreCase)
            ? parts[2].Trim()
            : null;

        result = new YtDlpFilePrintResult(path, height, ext);
        return true;
    }

    public static bool IsPostProcessingLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var trimmed = line.TrimStart();
        return trimmed.StartsWith("[Merger]", StringComparison.Ordinal) ||
               trimmed.StartsWith("[ExtractAudio]", StringComparison.Ordinal) ||
               trimmed.StartsWith("[EmbedThumbnail]", StringComparison.Ordinal) ||
               trimmed.StartsWith("[Metadata]", StringComparison.Ordinal) ||
               trimmed.StartsWith("[EmbedSubtitle]", StringComparison.Ordinal) ||
               trimmed.StartsWith("[SponsorBlock]", StringComparison.Ordinal) ||
               trimmed.StartsWith("[ModifyChapters]", StringComparison.Ordinal) ||
               trimmed.StartsWith("[Fixup", StringComparison.Ordinal);
    }

    private static long? ParseNullableLong(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "NA", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "None", StringComparison.OrdinalIgnoreCase))
            return null;

        if (long.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;

        if (double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var dbl) &&
            !double.IsNaN(dbl) && !double.IsInfinity(dbl) && dbl >= 0)
            return (long)dbl;

        return null;
    }

    private static int? ParseNullableInt(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "NA", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "None", StringComparison.OrdinalIgnoreCase))
            return null;

        return int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static double? ParseNullableDouble(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "NA", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "None", StringComparison.OrdinalIgnoreCase))
            return null;

        return double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
               !double.IsNaN(value) && !double.IsInfinity(value)
            ? value
            : null;
    }
}