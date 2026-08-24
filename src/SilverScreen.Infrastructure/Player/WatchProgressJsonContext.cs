using System.Text.Json.Serialization;

namespace SilverScreen.Infrastructure.Player;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Dictionary<string, WatchProgressEntry>), TypeInfoPropertyName = "WatchProgressEntries")]
[JsonSerializable(typeof(Dictionary<string, double>), TypeInfoPropertyName = "LegacyWatchProgressMap")]
internal sealed partial class WatchProgressJsonContext : JsonSerializerContext;