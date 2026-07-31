using System.Text.Json.Serialization;

namespace SilverScreen.Infrastructure.Features.Playback;

[JsonSerializable(typeof(Dictionary<string, double>), TypeInfoPropertyName = "WatchProgressMap")]
internal sealed partial class WatchProgressJsonContext : JsonSerializerContext;
