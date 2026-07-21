using System.Globalization;
using System.Text.Json;
using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Features.Search;

namespace SilverScreen.Infrastructure.YouTube;

internal static class YtDlpVideoParser
{
    public static IReadOnlyList<VideoSummary> Parse(string output)
    {
        var trimmedOutput = output.Trim();
        if (trimmedOutput.Length == 0) return [];

        if (trimmedOutput.StartsWith('{'))
            try
            {
                using var document = JsonDocument.Parse(trimmedOutput);
                return ParseRoot(document.RootElement);
            }
            catch (JsonException) when (trimmedOutput.Contains('\n'))
            {
                // yt-dlp can emit one JSON object per line depending on its output mode.
            }

        var videos = new List<VideoSummary>();
        foreach (var line in trimmedOutput.Split('\n',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var document = JsonDocument.Parse(line);
            videos.AddRange(ParseRoot(document.RootElement));
        }

        return videos;
    }

    private static IReadOnlyList<VideoSummary> ParseRoot(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("entries", out var entries)
            && entries.ValueKind == JsonValueKind.Array)
            return entries.EnumerateArray().Select(ParseVideo).OfType<VideoSummary>().ToArray();

        var video = ParseVideo(root);
        return video is null ? [] : [video];
    }

    private static VideoSummary? ParseVideo(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        var id = FirstString(element, "id", "display_id") ?? string.Empty;
        var rawUrl = FirstString(element, "webpage_url", "original_url", "url");
        var parsedUrl = YouTubeUrlParser.Parse(rawUrl);
        if (string.IsNullOrWhiteSpace(id) && parsedUrl.VideoId is not null) id = parsedUrl.VideoId;

        if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(rawUrl)) return null;

        var canonicalWatchUrl = PlaybackRequest.LooksLikeYouTubeVideoId(id)
            ? PlaybackRequest.BuildWatchUrl(id)
            : parsedUrl.CanonicalWatchUrl ?? rawUrl;

        return new VideoSummary(
            string.IsNullOrWhiteSpace(id) ? canonicalWatchUrl ?? "unknown" : id,
            FirstString(element, "title", "fulltitle") ?? "Untitled YouTube video",
            FirstString(element, "channel", "uploader", "channel_id", "uploader_id") ?? "YouTube",
            GetDuration(element),
            GetHighestQualityThumbnailUrl(element),
            IsShort(element, rawUrl),
            canonicalWatchUrl,
            GetApproximateUploadDate(element),
            GetPublishedAt(element));
    }

    private static bool IsShort(JsonElement element, string? rawUrl)
    {
        if (element.TryGetProperty("is_short", out var isShort)
            && isShort.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return isShort.GetBoolean();

        if (rawUrl?.Contains("/shorts/", StringComparison.OrdinalIgnoreCase) == true) return true;

        var title = FirstString(element, "title", "fulltitle");
        return title?.Contains("#shorts", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string? FirstString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
            if (element.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }

        return null;
    }

    private static DateOnly? GetApproximateUploadDate(JsonElement element)
    {
        if (element.TryGetProperty("upload_date", out var uploadDate)
            && uploadDate.ValueKind == JsonValueKind.String
            && DateOnly.TryParseExact(uploadDate.GetString(), "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsedUploadDate))
            return parsedUploadDate;

        var publishedAt = GetPublishedAt(element);
        return publishedAt is null ? null : DateOnly.FromDateTime(publishedAt.Value.UtcDateTime);
    }

    private static DateTimeOffset? GetPublishedAt(JsonElement element)
    {
        if (!element.TryGetProperty("timestamp", out var timestamp)
            || timestamp.ValueKind != JsonValueKind.Number
            || !timestamp.TryGetInt64(out var unixSeconds)
            || unixSeconds < 0)
            return null;

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static TimeSpan GetDuration(JsonElement element)
    {
        if (!element.TryGetProperty("duration", out var duration)) return TimeSpan.Zero;

        var seconds = duration.ValueKind switch
        {
            JsonValueKind.Number when duration.TryGetDouble(out var value) => value,
            JsonValueKind.String when double.TryParse(duration.GetString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var value) => value,
            _ => 0
        };

        return double.IsFinite(seconds) && seconds >= 0 && seconds <= TimeSpan.MaxValue.TotalSeconds
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;
    }

    private static string GetHighestQualityThumbnailUrl(JsonElement element)
    {
        if (!element.TryGetProperty("thumbnails", out var thumbnails) || thumbnails.ValueKind != JsonValueKind.Array)
            return FirstString(element, "thumbnail") ?? string.Empty;

        string? bestUrl = null;
        var bestArea = -1d;
        var bestPreference = int.MinValue;

        foreach (var thumbnail in thumbnails.EnumerateArray())
        {
            var url = FirstString(thumbnail, "url");
            if (url is null) continue;

            var preference = thumbnail.TryGetProperty("preference", out var preferenceProperty)
                             && preferenceProperty.ValueKind == JsonValueKind.Number
                             && preferenceProperty.TryGetInt32(out var value)
                ? value
                : int.MinValue;
            var width = thumbnail.TryGetProperty("width", out var widthProperty)
                        && widthProperty.ValueKind == JsonValueKind.Number
                        && widthProperty.TryGetDouble(out var widthValue)
                ? widthValue
                : -1;
            var height = thumbnail.TryGetProperty("height", out var heightProperty)
                         && heightProperty.ValueKind == JsonValueKind.Number
                         && heightProperty.TryGetDouble(out var heightValue)
                ? heightValue
                : -1;
            var area = width > 0 && height > 0 ? width * height : -1;

            if (bestUrl is null || preference > bestPreference || preference == bestPreference && area > bestArea)
            {
                bestUrl = url;
                bestPreference = preference;
                bestArea = area;
            }
        }

        return bestUrl ?? FirstString(element, "thumbnail") ?? string.Empty;
    }
}
