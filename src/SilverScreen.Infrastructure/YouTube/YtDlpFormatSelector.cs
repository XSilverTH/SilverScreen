using System.Globalization;
using System.Text.Json;
using SilverScreen.Core.Player;

namespace SilverScreen.Infrastructure.YouTube;

internal static class YtDlpFormatSelector
{
    public static ResolvedMedia? SelectMedia(string output, string preferredQuality)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        var details = YtDlpVideoParser.ParseDetails(output);
        var formats = ParseFormats(root);
        if (formats.Count == 0)
        {
            // If there's a direct url at root
            if (!root.TryGetProperty("url", out var directUrlProp) || directUrlProp.GetString() is not { } directUrl ||
                string.IsNullOrWhiteSpace(directUrl)) return null;
            var expiry = YouTubeMediaExpiryParser.TryExtractExpiry(directUrl);
            return new ResolvedMedia(directUrl, null, preferredQuality, expiry, details);
        }

        var maxTargetHeight = ParseTargetHeight(preferredQuality);

        // Separate formats into muxed, video-only, audio-only
        var muxed = formats.Where(f => f is { HasVideo: true, HasAudio: true }).ToList();
        var videoOnly = formats.Where(f => f is { HasVideo: true, HasAudio: false }).ToList();
        var audioOnly = formats.Where(f => f is { HasVideo: false, HasAudio: true }).ToList();

        // 1. Try best video-only + best audio-only
        ResolvedMediaStream? selectedVideo = null;
        if (videoOnly.Count > 0) selectedVideo = SelectBestVideoStream(videoOnly, maxTargetHeight);

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

        // 2. If separate video/audio not fully available, try muxed stream
        if (muxed.Count > 0)
        {
            var bestMuxed = SelectBestVideoStream(muxed, maxTargetHeight);
            if (bestMuxed is not null)
            {
                var expiry = YouTubeMediaExpiryParser.TryExtractExpiry(bestMuxed.Url);
                return new ResolvedMedia(
                    bestMuxed.Url,
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

    private static int? ParseTargetHeight(string quality)
    {
        return quality switch
        {
            "1080p" => 1080,
            "720p" => 720,
            "480p" => 480,
            "360p" => 360,
            _ => null // Best
        };
    }

    private static ResolvedMediaStream? SelectBestVideoStream(List<ResolvedMediaStream> videoStreams, int? maxHeight)
    {
        var eligible = maxHeight.HasValue
            ? [.. videoStreams.Where(v => v.Height.HasValue && v.Height.Value <= maxHeight.Value)]
            : videoStreams;

        // If nothing matches the <= maxHeight constraint (e.g. video only has higher or unstated height), fallback to all videoStreams
        if (eligible.Count == 0) eligible = videoStreams;

        return eligible
            .OrderByDescending(v => v.Height ?? 0)
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

            var url = GetString(format, "url");
            if (string.IsNullOrWhiteSpace(url)) continue;

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