using System.Text.Json;
using System.Text.Json.Serialization;
using SilverScreen.Core.Common;
using SilverScreen.Core.Downloads;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Infrastructure.Preferences;

/// <summary>
///     Statically generated <see cref="JsonSerializerContext" /> for application preferences.
/// </summary>
/// <remarks>
///     Explicitly registers <see cref="AppPreferences" />, <see cref="PlayerShortcutBindings" />,
///     <see cref="DownloadPreset" />, and <see cref="EquatableArray{T}" /> collections to ensure
///     zero runtime reflection or code generation during Native AOT publishing.
/// </remarks>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    Converters = [typeof(EquatableArrayStringJsonConverter), typeof(EquatableArrayDownloadPresetJsonConverter)])]
[JsonSerializable(typeof(AppPreferences))]
[JsonSerializable(typeof(ThemeMode))]
[JsonSerializable(typeof(VideoQuality))]
[JsonSerializable(typeof(PreferredVideoCodec))]
[JsonSerializable(typeof(PlaybackBackendKind))]
[JsonSerializable(typeof(PlayerShortcutBindings))]
[JsonSerializable(typeof(EquatableArray<string>))]
[JsonSerializable(typeof(DownloadPreset))]
[JsonSerializable(typeof(DownloadOptions))]
[JsonSerializable(typeof(EquatableArray<DownloadPreset>))]
[JsonSerializable(typeof(DownloadDestinationKind))]
[JsonSerializable(typeof(DownloadMediaMode))]
[JsonSerializable(typeof(DownloadContainerFormat))]
[JsonSerializable(typeof(DownloadSubtitleMode))]
[JsonSerializable(typeof(SponsorBlockDownloadMode))]
internal sealed partial class PreferencesJsonContext : JsonSerializerContext;

internal sealed class EquatableArrayDownloadPresetJsonConverter : JsonConverter<EquatableArray<DownloadPreset>>
{
    public override EquatableArray<DownloadPreset> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return EquatableArray<DownloadPreset>.Empty;

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"Expected StartArray token, got {reader.TokenType}");

        var list = new List<DownloadPreset>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return list.Count == 0
                    ? EquatableArray<DownloadPreset>.Empty
                    : new EquatableArray<DownloadPreset>(list.ToArray());

            if (reader.TokenType == JsonTokenType.Null)
                continue;

            var preset = JsonSerializer.Deserialize(ref reader, PreferencesJsonContext.Default.DownloadPreset);
            if (preset is not null)
                list.Add(preset);
        }

        throw new JsonException("Expected EndArray token, but reached end of input");
    }

    public override void Write(
        Utf8JsonWriter writer,
        EquatableArray<DownloadPreset> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value.AsSpan())
            JsonSerializer.Serialize(writer, item, PreferencesJsonContext.Default.DownloadPreset);
        writer.WriteEndArray();
    }
}
