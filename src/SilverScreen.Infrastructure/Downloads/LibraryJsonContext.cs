using System.Text.Json.Serialization;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Downloads;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Infrastructure.Downloads;

internal sealed record PersistedLibraryIndex(int Version, LibraryEntry[] Entries);

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(PersistedLibraryIndex))]
[JsonSerializable(typeof(LibraryEntry))]
[JsonSerializable(typeof(LibraryEntry[]))]
[JsonSerializable(typeof(VideoSummary))]
[JsonSerializable(typeof(YouTubePlaybackProgress))]
[JsonSerializable(typeof(SponsorBlockSegment))]
[JsonSerializable(typeof(SponsorBlockSegment[]))]
[JsonSerializable(typeof(DownloadMediaMode))]
[JsonSerializable(typeof(VideoQuality))]
[JsonSerializable(typeof(DownloadSubtitleMode))]
internal sealed partial class LibraryJsonContext : JsonSerializerContext;
