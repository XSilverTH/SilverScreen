using System.Text.RegularExpressions;
using SilverScreen.Core.Preferences;
using System.Globalization;
using System.Text.Json;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Player;

namespace SilverScreen.Infrastructure.YouTube;

/// <summary>
///     Evaluates and selects formats from yt-dlp JSON dumps (<c>--dump-single-json</c>), and provides
///     the mapping from quality preference labels to mpv/yt-dlp format strings.
/// </summary>
/// <remarks>
///     <para>
///         <b>Architectural Role:</b><br />
///         This class fulfills two distinct roles:
///         <list type="bullet">
///             <item>
///                 <description>
///                     <b>Active:</b> <see cref="ToMpvFormat(string)" /> provides the single source of truth mapping
///                     user-configured
///                     video quality labels (e.g. "1080p", "720p") to yt-dlp format specifiers utilized by mpv command
///                     builders.
///                 </description>
///             </item>
///             <item>
///                 <description>
///                     <b>Dormant Fallback Extraction:</b>
///                     <see cref="SelectMedia(string, string, YouTubeVideoDetails?)" /> contains
///                     the format selection engine that parses adaptive video/audio streams, muxed streams, and live HLS
///                     manifests,
///                     pairing streams and extracting expiration timestamps. Although standard playback uses mpv's
///                     internal
///                     <c>ytdl_hook.lua</c>, this logic is retained as a fallback extraction pipeline and for future
///                     offline downloading
///                     or headless stream resolution.
///                 </description>
///             </item>
///         </list>
///     </para>
/// </remarks>
public static class YtDlpFormatSelector
{
    /// <summary>
    ///     Single source of truth mapping a quality label to the yt-dlp/mpv format selector.
    ///     Null means "Best" (leave the player default in place). Pure: no I/O.
    /// </summary>
    public static string? ToMpvFormat(string preferredQuality, PreferredVideoCodec preferredCodec = PreferredVideoCodec.Any)
    {
        var targetHeight = ParseTargetHeight(preferredQuality);
        var codecPrefix = BuildCodecTargetFormat(preferredCodec, targetHeight);

        if (targetHeight is null)
        {
            if (codecPrefix is null)
                return null;

            return $"{codecPrefix}bestvideo+bestaudio/best";
        }

        var baseFormat = $"bestvideo[height<={targetHeight.Value}]+bestaudio/best[height<={targetHeight.Value}]";
        return codecPrefix is not null ? $"{codecPrefix}{baseFormat}" : baseFormat;
    }

    private static string? BuildCodecTargetFormat(PreferredVideoCodec codec, int? targetHeight)
    {
        var heightCondition = targetHeight.HasValue ? $"[height<={targetHeight.Value}]" : string.Empty;

        return codec switch
        {
            PreferredVideoCodec.Av1 => $"bestvideo[vcodec^=av01]{heightCondition}+bestaudio/",
            PreferredVideoCodec.Vp9 => $"bestvideo[vcodec^=vp09]{heightCondition}+bestaudio/bestvideo[vcodec^=vp9]{heightCondition}+bestaudio/",
            PreferredVideoCodec.H264 => $"bestvideo[vcodec^=avc1]{heightCondition}+bestaudio/bestvideo[vcodec^=h264]{heightCondition}+bestaudio/",
            _ => null
        };
    }

    public static ResolvedMedia? SelectMedia(
        string output,
        string preferredQuality,
        YouTubeVideoDetails? details = null,
        PreferredVideoCodec preferredCodec = PreferredVideoCodec.Any)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(output);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var formats = ParseFormats(root);
            if (formats.Count == 0)
            {
                // If there's a direct url at root
                if (!root.TryGetProperty("url", out var directUrlProp) ||
                    directUrlProp.GetString() is not { } directUrl ||
                    string.IsNullOrWhiteSpace(directUrl)) return null;
                var expiry = YouTubeMediaExpiryParser.TryExtractExpiry(directUrl);
                return new ResolvedMedia(directUrl, null, preferredQuality, expiry, details);
            }

            var maxTargetHeight = ParseTargetHeight(preferredQuality);

            // Separate formats into muxed, video-only, audio-only
            var muxed = formats.Where(f => f is { HasVideo: true, HasAudio: true } && !string.IsNullOrWhiteSpace(f.Url)).ToList();
            var videoOnly = formats.Where(f => f is { HasVideo: true, HasAudio: false } && !string.IsNullOrWhiteSpace(f.Url)).ToList();
            var audioOnly = formats.Where(f => f is { HasVideo: false, HasAudio: true } && !string.IsNullOrWhiteSpace(f.Url)).ToList();

            // Prefer streams that satisfy the quality cap before relaxing the adaptive selection.
            // This avoids choosing an over-cap video-only stream when a compliant muxed stream exists.
            ResolvedMediaStream? selectedVideo = null;
            if (videoOnly.Count > 0) selectedVideo = SelectBestVideoStream(videoOnly, maxTargetHeight, false, preferredCodec);

            ResolvedMediaStream? selectedAudio = null;
            if (audioOnly.Count > 0) selectedAudio = SelectBestAudioStream(audioOnly);

            if (selectedVideo is not null && selectedAudio is not null)
            {
                var expiry = MinExpiry(
                    YouTubeMediaExpiryParser.TryExtractExpiry(selectedVideo.Url),
                    YouTubeMediaExpiryParser.TryExtractExpiry(selectedAudio.Url));

                return new ResolvedMedia(
                    selectedVideo.Url,
                    selectedAudio.Url,
                    preferredQuality,
                    expiry,
                    details);
            }

            // A compliant muxed stream is preferable to relaxing the adaptive video selection.
            var selectedMuxed = muxed.Count > 0
                ? SelectBestVideoStream(muxed, maxTargetHeight, false, preferredCodec)
                : null;
            if (selectedMuxed is not null)
            {
                var expiry = YouTubeMediaExpiryParser.TryExtractExpiry(selectedMuxed.Url);
                return new ResolvedMedia(
                    selectedMuxed.Url,
                    null,
                    preferredQuality,
                    expiry,
                    details);
            }

            // No compliant stream is available, so relax the cap for the existing fallback behavior.
            if (videoOnly.Count > 0) selectedVideo = SelectBestVideoStream(videoOnly, maxTargetHeight, true, preferredCodec);

            if (selectedVideo is not null && selectedAudio is not null)
            {
                var expiry = MinExpiry(
                    YouTubeMediaExpiryParser.TryExtractExpiry(selectedVideo.Url),
                    YouTubeMediaExpiryParser.TryExtractExpiry(selectedAudio.Url));

                return new ResolvedMedia(
                    selectedVideo.Url,
                    selectedAudio.Url,
                    preferredQuality,
                    expiry,
                    details);
            }

            if (muxed.Count > 0)
            {
                selectedMuxed = SelectBestVideoStream(muxed, maxTargetHeight, true, preferredCodec);
                if (selectedMuxed is not null)
                {
                    var expiry = YouTubeMediaExpiryParser.TryExtractExpiry(selectedMuxed.Url);
                    return new ResolvedMedia(
                        selectedMuxed.Url,
                        null,
                        preferredQuality,
                        expiry,
                        details);
                }
            }

            // 3. If only video or only audio or fallback
            var fallback = selectedVideo ?? (muxed.Count > 0 ? muxed[0] : formats.Count > 0 ? formats[0] : null);
            if (fallback is null) return null;
            {
                var expiry = YouTubeMediaExpiryParser.TryExtractExpiry(fallback.Url);
                return new ResolvedMedia(
                    fallback.Url,
                    selectedAudio?.Url,
                    preferredQuality,
                    expiry,
                    details);
            }
        }
    }

    public static int? ParseTargetHeight(string? quality)
    {
        if (string.IsNullOrWhiteSpace(quality))
            return null;

        var trimmed = quality.Trim();
        if (string.Equals(trimmed, "Best", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Auto", StringComparison.OrdinalIgnoreCase))
            return null;

        if (string.Equals(trimmed, "8k", StringComparison.OrdinalIgnoreCase)) return 4320;
        if (string.Equals(trimmed, "4k", StringComparison.OrdinalIgnoreCase)) return 2160;
        if (string.Equals(trimmed, "2k", StringComparison.OrdinalIgnoreCase)) return 1440;

        var match = Regex.Match(trimmed, @"(?<height>\d{2,4})p", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups["height"].ValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h))
            return h;

        var bareNumberMatch = Regex.Match(trimmed, @"^\d{2,4}$");
        if (bareNumberMatch.Success && int.TryParse(bareNumberMatch.ValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bareH))
            return bareH;

        if (trimmed.Contains("8K", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("4320")) return 4320;
        if (trimmed.Contains("4K", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("2160")) return 2160;
        if (trimmed.Contains("2K", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("1440")) return 1440;

        return null;
    }

    public static IReadOnlyList<string> GetAvailableQualityTracks(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return [];

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(output);
        }
        catch (JsonException)
        {
            return [];
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return [];

            var formats = ParseFormats(root);
            if (formats.Count == 0) return [];

            var videoStreams = formats
                .Where(f => f.HasVideo && f.Height is > 0)
                .ToList();

            if (videoStreams.Count == 0) return [];

            var tracks = new List<string>();
            var groupedByHeight = videoStreams
                .GroupBy(v => v.Height!.Value)
                .OrderByDescending(g => g.Key);

            foreach (var group in groupedByHeight)
            {
                var height = group.Key;
                var maxFps = group.Max(v => v.Fps) ?? 0;
                var fpsSuffix = maxFps > 30 ? $"{maxFps}" : string.Empty;

                var label = $"{height}p{fpsSuffix}";

                tracks.Add(label);
            }

            return tracks;
        }
    }

    private static bool MatchesCodec(string? videoCodec, PreferredVideoCodec preferred)
    {
        if (string.IsNullOrWhiteSpace(videoCodec) || preferred == PreferredVideoCodec.Any)
            return false;

        return preferred switch
        {
            PreferredVideoCodec.Av1 => videoCodec.StartsWith("av01", StringComparison.OrdinalIgnoreCase) ||
                                       videoCodec.StartsWith("av1", StringComparison.OrdinalIgnoreCase),
            PreferredVideoCodec.Vp9 => videoCodec.StartsWith("vp9", StringComparison.OrdinalIgnoreCase) ||
                                       videoCodec.StartsWith("vp09", StringComparison.OrdinalIgnoreCase),
            PreferredVideoCodec.H264 => videoCodec.StartsWith("avc1", StringComparison.OrdinalIgnoreCase) ||
                                        videoCodec.StartsWith("h264", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static ResolvedMediaStream? SelectBestVideoStream(
        List<ResolvedMediaStream> videoStreams,
        int? maxHeight,
        bool allowRelaxation = true,
        PreferredVideoCodec preferredCodec = PreferredVideoCodec.Any)
    {
        var eligible = maxHeight.HasValue
            ? [.. videoStreams.Where(v => v.Height.HasValue && v.Height.Value <= maxHeight.Value)]
            : videoStreams;

        // If nothing matches the <= maxHeight constraint (e.g. video only has higher or unstated height), fallback to all videoStreams
        if (eligible.Count == 0)
        {
            if (!allowRelaxation) return null;
            eligible = videoStreams;
        }

        return eligible
            .OrderByDescending(v => MatchesCodec(v.VideoCodec, preferredCodec) ? 1 : 0)
            .ThenByDescending(v => v.Height ?? 0)
            .ThenByDescending(v => v.Fps ?? 0)
            .ThenByDescending(v => v.Bitrate ?? 0)
            .FirstOrDefault();
    }

    private static ResolvedMediaStream? SelectBestAudioStream(List<ResolvedMediaStream> audioStreams)
    {
        return audioStreams
            .OrderByDescending(a => a.Bitrate ?? 0)
            .FirstOrDefault();
    }

    private static List<ResolvedMediaStream> ParseFormats(JsonElement root)
    {
        var list = new List<ResolvedMediaStream>();
        if (!root.TryGetProperty("formats", out var formatsProp) || formatsProp.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var format in formatsProp.EnumerateArray())
        {
            if (format.ValueKind != JsonValueKind.Object) continue;

            var url = GetString(format, "url") ?? GetString(format, "manifest_url") ?? string.Empty;

            // Skip DRM or storyboards (mhtml, etc.)
            if (format.TryGetProperty("has_drm", out var hasDrm) && hasDrm.ValueKind == JsonValueKind.True &&
                hasDrm.GetBoolean())
                continue;

            var formatNote = GetString(format, "format_note");
            if (string.Equals(formatNote, "storyboard", StringComparison.OrdinalIgnoreCase))
                continue;

            var protocol = GetString(format, "protocol");
            if (string.Equals(protocol, "mhtml", StringComparison.OrdinalIgnoreCase))
                continue;

            var vcodec = GetString(format, "vcodec");
            var acodec = GetString(format, "acodec");

            var hasVideo = !string.IsNullOrWhiteSpace(vcodec) &&
                           !string.Equals(vcodec, "none", StringComparison.OrdinalIgnoreCase);
            var hasAudio = !string.IsNullOrWhiteSpace(acodec) &&
                           !string.Equals(acodec, "none", StringComparison.OrdinalIgnoreCase);

            // If neither hasVideo nor hasAudio is indicated by codec, check height / abr
            var height = GetInt(format, "height");
            var width = GetInt(format, "width");
            var fps = GetInt(format, "fps");
            var tbr = GetDouble(format, "tbr") ?? GetDouble(format, "vbr") ?? GetDouble(format, "abr");

            if (!hasVideo && height is > 0)
                hasVideo = true;

            if (!hasVideo && !hasAudio)
            {
                // Unspecified, check ext
                var ext = GetString(format, "ext");
                switch (ext)
                {
                    case "mp4" or "webm" or "mkv":
                        hasVideo = true;
                        break;
                    case "m4a" or "opus" or "mp3" or "webm_audio":
                        hasAudio = true;
                        break;
                }
            }

            var formatId = GetString(format, "format_id");

            list.Add(new ResolvedMediaStream(
                url,
                formatId,
                height,
                width,
                fps,
                vcodec,
                acodec,
                tbr,
                hasVideo,
                hasAudio));
        }

        return list;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop)) return null;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var num)) return num;
        if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var strNum)) return strNum;
        return null;
    }

    private static double? GetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop)) return null;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var num)) return num;
        if (prop.ValueKind == JsonValueKind.String && double.TryParse(prop.GetString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var strNum)) return strNum;
        return null;
    }

    private static DateTimeOffset? MinExpiry(DateTimeOffset? a, DateTimeOffset? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return a < b ? a : b;
    }
}