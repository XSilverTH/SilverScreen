using System.Text.Json.Serialization;

namespace SilverScreen.Infrastructure.Common;

internal sealed record PersistedAccountProfile(
    string SessionHash,
    string DisplayName,
    string? AvatarUrl,
    DateTimeOffset CachedAtUtc);

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(PersistedAccountProfile))]
internal sealed partial class PersistentCacheJsonContext : JsonSerializerContext;