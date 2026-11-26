using System.Text.Json.Serialization;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Core.Downloads;

/// <summary>
///     Complete set of user-configurable options for a YouTube download or saved preset.
/// </summary>
public sealed record DownloadOptions
{
    [JsonConverter(typeof(DownloadDestinationKindConverter))]
    public DownloadDestinationKind DestinationKind { get; init; } = DownloadDestinationKind.Library;

    public string? CustomFolderPath { get; init; }

    [JsonConverter(typeof(DownloadMediaModeConverter))]
    public DownloadMediaMode MediaMode { get; init; } = DownloadMediaMode.VideoAndAudio;

    [JsonConverter(typeof(VideoQualityConverter))]
    public VideoQuality Quality { get; init; } = VideoQuality.Best;

    [JsonConverter(typeof(PreferredVideoCodecConverter))]
    public PreferredVideoCodec PreferredCodec { get; init; } = PreferredVideoCodec.Any;

    [JsonConverter(typeof(DownloadContainerFormatConverter))]
    public DownloadContainerFormat Container { get; init; } = DownloadContainerFormat.Auto;

    [JsonConverter(typeof(DownloadSubtitleModeConverter))]
    public DownloadSubtitleMode SubtitleMode { get; init; } = DownloadSubtitleMode.None;

    public string SubtitleLanguage { get; init; } = string.Empty;

    public bool EmbedMetadata { get; init; } = true;

    public bool EmbedThumbnail { get; init; } = true;

    [JsonConverter(typeof(SponsorBlockDownloadModeConverter))]
    public SponsorBlockDownloadMode SponsorBlockMode { get; init; } = SponsorBlockDownloadMode.MarkAsChapters;

    public DownloadOptions Normalized()
    {
        return this with
        {
            CustomFolderPath = DestinationKind == DownloadDestinationKind.CustomFolder
                ? CustomFolderPath?.Trim()
                : null,
            Container = Container.NormalizeForMode(MediaMode),
            SubtitleLanguage = SubtitleLanguage?.Trim() ?? string.Empty
        };
    }

    public string ResolveTargetDirectory(string libraryDirectory)
    {
        return DestinationKind == DownloadDestinationKind.CustomFolder &&
               !string.IsNullOrWhiteSpace(CustomFolderPath)
            ? CustomFolderPath.Trim()
            : libraryDirectory;
    }

    public string FormatSummary()
    {
        var parts = new List<string>(5);
        parts.Add(DestinationKind == DownloadDestinationKind.Library
            ? "Library"
            : string.IsNullOrWhiteSpace(CustomFolderPath)
                ? "Custom folder"
                : Path.GetFileName(CustomFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } dirName
                    ? dirName
                    : CustomFolderPath);

        if (MediaMode == DownloadMediaMode.AudioOnly)
        {
            parts.Add("Audio");
        }
        else
        {
            parts.Add(Quality.ToPersistedString());
            if (PreferredCodec != PreferredVideoCodec.Any)
                parts.Add(PreferredCodec.ToPersistedString());
        }

        var normalizedContainer = Container.NormalizeForMode(MediaMode);
        if (normalizedContainer != DownloadContainerFormat.Auto)
            parts.Add(normalizedContainer.ToDisplayString());

        if (SubtitleMode != DownloadSubtitleMode.None)
            parts.Add(SubtitleMode == DownloadSubtitleMode.Embedded ? "Embedded subs" : "Sidecar subs");

        if (DestinationKind == DownloadDestinationKind.CustomFolder)
            parts.Add(SponsorBlockMode switch
            {
                SponsorBlockDownloadMode.MarkAsChapters => "SB chapters",
                SponsorBlockDownloadMode.Strip => "SB strip",
                _ => "SB off"
            });

        return string.Join(" · ", parts);
    }
}

/// <summary>
///     Named download configuration saved by the user for one-click or menu access.
/// </summary>
public sealed record DownloadPreset(
    string Id,
    string Name,
    DownloadOptions Options);
