using System.Text.Json;
using System.Text.Json.Serialization;
using SilverScreen.Core.Player;

namespace SilverScreen.Core.Preferences;

/// <summary>
///     Application color-scheme preference.
///     Persisted as "System", "Light", or "Dark" (see <see cref="ThemeModes" />).
/// </summary>
public enum ThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2
}

/// <summary>
///     Preferred video quality.
///     Persisted as "Best", "1080p", "720p", "480p", or "360p" (see <see cref="VideoQualities" />).
/// </summary>
public enum VideoQuality
{
    Best = 0,
    P1080 = 1,
    P720 = 2,
    P480 = 3,
    P360 = 4
}

/// <summary>
///     Playback engine preference.
///     Persisted as "Embedded Player" or "External MPV" (see <see cref="PlaybackBackends" />).
/// </summary>
public enum PlaybackBackendKind
{
    Embedded = 0,
    ExternalMpv = 1
}

/// <summary>
///     Persisted-string mapping for <see cref="ThemeMode" />.
///     Reads are case-insensitive; unknown values fall back to <see cref="ThemeMode.System" />.
/// </summary>
public static class ThemeModes
{
    public static string ToPersistedString(this ThemeMode mode)
    {
        return mode switch
        {
            ThemeMode.Light => "Light",
            ThemeMode.Dark => "Dark",
            _ => "System"
        };
    }

    public static ThemeMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ThemeMode.System;

        foreach (var mode in Enum.GetValues<ThemeMode>())
            if (string.Equals(value.Trim(), mode.ToPersistedString(), StringComparison.OrdinalIgnoreCase))
                return mode;

        return ThemeMode.System;
    }
}

/// <summary>
///     Persisted-string mapping for <see cref="VideoQuality" />.
///     Reads are case-insensitive; unknown values fall back to <see cref="VideoQuality.Best" />.
/// </summary>
public static class VideoQualities
{
    public static string ToPersistedString(this VideoQuality quality)
    {
        return quality switch
        {
            VideoQuality.P1080 => "1080p",
            VideoQuality.P720 => "720p",
            VideoQuality.P480 => "480p",
            VideoQuality.P360 => "360p",
            _ => "Best"
        };
    }

    public static VideoQuality Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return VideoQuality.Best;

        foreach (var quality in Enum.GetValues<VideoQuality>())
            if (string.Equals(value.Trim(), quality.ToPersistedString(), StringComparison.OrdinalIgnoreCase))
                return quality;

        return VideoQuality.Best;
    }
}

public sealed class ThemeModeConverter : JsonConverter<ThemeMode>
{
    public override ThemeMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.String
            ? ThemeModes.Parse(reader.GetString())
            : ThemeMode.System;
    }

    public override void Write(Utf8JsonWriter writer, ThemeMode value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToPersistedString());
    }
}

public sealed class VideoQualityConverter : JsonConverter<VideoQuality>
{
    public override VideoQuality Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.String
            ? VideoQualities.Parse(reader.GetString())
            : VideoQuality.Best;
    }

    public override void Write(Utf8JsonWriter writer, VideoQuality value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToPersistedString());
    }
}

public sealed class PlaybackBackendKindConverter : JsonConverter<PlaybackBackendKind>
{
    public override PlaybackBackendKind Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.String
            ? PlaybackBackends.Parse(reader.GetString())
            : PlaybackBackendKind.Embedded;
    }

    public override void Write(Utf8JsonWriter writer, PlaybackBackendKind value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToPersistedString());
    }
}
