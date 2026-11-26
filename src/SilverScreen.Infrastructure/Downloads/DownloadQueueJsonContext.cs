using System.Text.Json.Serialization;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Downloads;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Infrastructure.Downloads;

internal sealed record PersistedDownloadQueue(int Version, DownloadJob[] Jobs);

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(PersistedDownloadQueue))]
[JsonSerializable(typeof(DownloadJob))]
[JsonSerializable(typeof(DownloadJob[]))]
[JsonSerializable(typeof(DownloadOptions))]
[JsonSerializable(typeof(VideoSummary))]
[JsonSerializable(typeof(YouTubePlaybackProgress))]
[JsonSerializable(typeof(DownloadDestinationKind))]
[JsonSerializable(typeof(DownloadMediaMode))]
[JsonSerializable(typeof(VideoQuality))]
[JsonSerializable(typeof(PreferredVideoCodec))]
[JsonSerializable(typeof(DownloadContainerFormat))]
[JsonSerializable(typeof(DownloadSubtitleMode))]
[JsonSerializable(typeof(SponsorBlockDownloadMode))]
[JsonSerializable(typeof(DownloadJobStatus))]
internal sealed partial class DownloadQueueJsonContext : JsonSerializerContext;