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
///     Persisted as "Best", "4320p", "2160p", "1440p", "1080p", "720p", "480p", "360p", "240p", or "144p" (see <see cref="VideoQualities" />).
/// </summary>
public enum VideoQuality
{
    Best = 0,
    P4320 = 1,
    P2160 = 2,
    P1440 = 3,
    P1080 = 4,
    P720 = 5,
    P480 = 6,
    P360 = 7,
    P240 = 8,
    P144 = 9
}

/// <summary>
///     Preferred video codec for playback.
///     Persisted as "Any", "AV1", "VP9", or "H.264" (see <see cref="PreferredVideoCodecs" />).
/// </summary>
public enum PreferredVideoCodec
{
    Any = 0,
    Av1 = 1,
    Vp9 = 2,
    H264 = 3
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
            VideoQuality.P4320 => "4320p",
            VideoQuality.P2160 => "2160p",
            VideoQuality.P1440 => "1440p",
            VideoQuality.P1080 => "1080p",
            VideoQuality.P720 => "720p",
            VideoQuality.P480 => "480p",
            VideoQuality.P360 => "360p",
            VideoQuality.P240 => "240p",
            VideoQuality.P144 => "144p",
            _ => "Best"
        };
    }

    public static VideoQuality Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return VideoQuality.Best;

        var trimmed = value.Trim();
        if (string.Equals(trimmed, "8k", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "4320", StringComparison.OrdinalIgnoreCase))
            return VideoQuality.P4320;
        if (string.Equals(trimmed, "4k", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "2160", StringComparison.OrdinalIgnoreCase))
            return VideoQuality.P2160;
        if (string.Equals(trimmed, "2k", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "1440", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "qhd", StringComparison.OrdinalIgnoreCase))
            return VideoQuality.P1440;
        if (string.Equals(trimmed, "1080", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "fhd", StringComparison.OrdinalIgnoreCase))
            return VideoQuality.P1080;
        if (string.Equals(trimmed, "720", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "hd", StringComparison.OrdinalIgnoreCase))
            return VideoQuality.P720;
        if (string.Equals(trimmed, "480", StringComparison.OrdinalIgnoreCase))
            return VideoQuality.P480;
        if (string.Equals(trimmed, "360", StringComparison.OrdinalIgnoreCase))
            return VideoQuality.P360;
        if (string.Equals(trimmed, "240", StringComparison.OrdinalIgnoreCase))
            return VideoQuality.P240;
        if (string.Equals(trimmed, "144", StringComparison.OrdinalIgnoreCase))
            return VideoQuality.P144;

        foreach (var quality in Enum.GetValues<VideoQuality>())
            if (string.Equals(trimmed, quality.ToPersistedString(), StringComparison.OrdinalIgnoreCase))
                return quality;

        return VideoQuality.Best;
    }
}

/// <summary>
///     Persisted-string mapping for <see cref="PreferredVideoCodec" />.
///     Reads are case-insensitive; unknown values fall back to <see cref="PreferredVideoCodec.Any" />.
/// </summary>
public static class PreferredVideoCodecs
{
    public static string ToPersistedString(this PreferredVideoCodec codec)
    {
        return codec switch
        {
            PreferredVideoCodec.Av1 => "AV1",
            PreferredVideoCodec.Vp9 => "VP9",
            PreferredVideoCodec.H264 => "H.264",
            _ => "Any"
        };
    }

    public static string ToDisplayString(this PreferredVideoCodec codec)
    {
        return codec switch
        {
            PreferredVideoCodec.Av1 => "AV1",
            PreferredVideoCodec.Vp9 => "VP9",
            PreferredVideoCodec.H264 => "H.264 / AVC",
            _ => "No preference"
        };
    }

    public static PreferredVideoCodec Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return PreferredVideoCodec.Any;

        var trimmed = value.Trim();
        if (string.Equals(trimmed, "AV1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "av01", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "av1", StringComparison.OrdinalIgnoreCase))
            return PreferredVideoCodec.Av1;
        if (string.Equals(trimmed, "VP9", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "vp09", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "vp9", StringComparison.OrdinalIgnoreCase))
            return PreferredVideoCodec.Vp9;
        if (string.Equals(trimmed, "H.264", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "H264", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "h.264", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "h264", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "AVC", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "avc", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "avc1", StringComparison.OrdinalIgnoreCase))
            return PreferredVideoCodec.H264;

        return PreferredVideoCodec.Any;
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

public sealed class PreferredVideoCodecConverter : JsonConverter<PreferredVideoCodec>
{
    public override PreferredVideoCodec Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.String
            ? PreferredVideoCodecs.Parse(reader.GetString())
            : PreferredVideoCodec.Any;
    }

    public override void Write(Utf8JsonWriter writer, PreferredVideoCodec value, JsonSerializerOptions options)
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