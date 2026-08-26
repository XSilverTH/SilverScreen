using System.Globalization;
using System.Text.Json;
using SilverScreen.Core.Browsing.Subscriptions;

namespace SilverScreen.Infrastructure.YouTube;

internal static class YtDlpSubscriptionsParser
{
    public static IReadOnlyList<SubscribedChannel> ParseChannels(string output)
    {
        var trimmedOutput = output.Trim();
        if (trimmedOutput.Length == 0) return [];

        if (trimmedOutput.StartsWith('{'))
            try
            {
                using var document = JsonDocument.Parse(trimmedOutput);
                return ParseChannelsRoot(document.RootElement);
            }
            catch (JsonException) when (trimmedOutput.Contains('\n'))
            {
                // yt-dlp can emit one JSON object per line depending on output mode.
            }

        var channels = new List<SubscribedChannel>();
        foreach (var line in trimmedOutput.Split('\n',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var document = JsonDocument.Parse(line);
            channels.AddRange(ParseChannelsRoot(document.RootElement));
        }

        return channels;
    }

    private static SubscribedChannel[] ParseChannelsRoot(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("entries", out var entries)
            && entries.ValueKind == JsonValueKind.Array)
            return [.. entries.EnumerateArray().Select(ParseChannel).OfType<SubscribedChannel>()];

        var channel = ParseChannel(root);
        return channel is null ? [] : [channel];
    }

    private static SubscribedChannel? ParseChannel(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        var id = FirstString(element, "id", "channel_id", "uploader_id") ?? string.Empty;
        var title = FirstString(element, "title", "channel", "uploader", "fulltitle") ?? "Unknown Channel";
        var rawUrl = FirstString(element, "url", "channel_url", "uploader_url", "webpage_url");
        var channelUrl = NormalizeChannelUrl(rawUrl, id);

        if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(channelUrl))
            return null;

        var avatarUrl = GetThumbnailUrl(element);
        var description = FirstString(element, "description");
        var subscriberCount = GetInt64(element, "channel_follower_count", "subscriber_count", "follower_count");

        return new SubscribedChannel(
            id,
            title,
            channelUrl,
            avatarUrl,
            description,
            subscriberCount);
    }

    private static string NormalizeChannelUrl(string? rawUrl, string id)
    {
        if (!string.IsNullOrWhiteSpace(rawUrl))
            // if (rawUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            //     rawUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            //     return rawUrl;
            //
            // if (rawUrl.StartsWith('/'))
            //     return $"https://www.youtube.com{rawUrl}";
            //
            // return $"https://www.youtube.com/{rawUrl}";
            return
                !(rawUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                  rawUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    ? rawUrl.StartsWith('/')
                        ? $"https://www.youtube.com{rawUrl}"
                        : $"https://www.youtube.com/{rawUrl}"
                    : rawUrl;

        if (id.StartsWith("UC", StringComparison.Ordinal))
            return $"https://www.youtube.com/channel/{Uri.EscapeDataString(id)}";

        if (id.StartsWith('@'))
            return $"https://www.youtube.com/@{Uri.EscapeDataString(id.TrimStart('@'))}";

        return !string.IsNullOrWhiteSpace(id)
            ? $"https://www.youtube.com/channel/{Uri.EscapeDataString(id)}"
            : string.Empty;
    }

    private static string? GetThumbnailUrl(JsonElement element)
    {
        if (element.TryGetProperty("thumbnails", out var thumbnails) && thumbnails.ValueKind == JsonValueKind.Array)
        {
            string? highestUrl = null;
            var highestPreference = int.MinValue;
            var highestArea = -1d;
            foreach (var thumbnail in thumbnails.EnumerateArray())
            {
                if (thumbnail.ValueKind != JsonValueKind.Object)
                    continue;

                var url = FirstString(thumbnail, "url");
                if (string.IsNullOrWhiteSpace(url))
                    continue;

                var preference = thumbnail.TryGetProperty("preference", out var pref) && pref.TryGetInt32(out var p)
                    ? p
                    : 0;

                var width = thumbnail.TryGetProperty("width", out var widthProp) && widthProp.TryGetDouble(out var w)
                    ? w
                    : -1;
                var height = thumbnail.TryGetProperty("height", out var heightProp) &&
                             heightProp.TryGetDouble(out var h)
                    ? h
                    : -1;
                var area = width > 0 && height > 0 ? width * height : -1;

                if (highestUrl is not null && preference <= highestPreference &&
                    (preference != highestPreference || !(area > highestArea))) continue;
                highestPreference = preference;
                highestArea = area;
                highestUrl = url;
            }

            if (!string.IsNullOrWhiteSpace(highestUrl))
                return NormalizeUrl(highestUrl);
        }

        var fallback = FirstString(element, "thumbnail", "avatar", "channel_thumbnail", "uploader_thumbnail");
        return !string.IsNullOrWhiteSpace(fallback) ? NormalizeUrl(fallback) : null;
    }

    private static string NormalizeUrl(string url)
    {
        return url.StartsWith("//", StringComparison.Ordinal) ? $"https:{url}" : url;
    }

    private static long? GetInt64(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var property))
                continue;

            switch (property.ValueKind)
            {
                case JsonValueKind.Number when property.TryGetInt64(out var value):
                    return value;
                case JsonValueKind.String when long.TryParse(property.GetString(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var value):
                    return value;
                case JsonValueKind.Undefined:
                case JsonValueKind.Object:
                case JsonValueKind.Array:
                case JsonValueKind.True:
                case JsonValueKind.False:
                case JsonValueKind.Null:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return null;
    }

    private static string? FirstString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var property))
                continue;

            if (property.ValueKind == JsonValueKind.String)
                return property.GetString();
        }

        return null;
    }
}