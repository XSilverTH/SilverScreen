using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Features.Search;

public sealed class YtDlpSearchService : ISearchService
{
    private readonly IPreferencesService? _preferencesService;
    private readonly IYtDlpRunner _runner;
    private readonly YtDlpOptions _staticOptions;

    public YtDlpSearchService()
        : this(new YtDlpOptions(), new YtDlpRunner())
    {
    }

    public YtDlpSearchService(YtDlpOptions options, IYtDlpRunner runner)
    {
        _staticOptions = options;
        _runner = runner;
        _preferencesService = null;
    }

    public YtDlpSearchService(IPreferencesService preferencesService, IYtDlpRunner runner)
    {
        _staticOptions = new YtDlpOptions();
        _runner = runner;
        _preferencesService = preferencesService;
    }

    public async Task<SearchResultPage> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query)) return SearchResultPage.Empty;

        try
        {
            var activeOptions = GetActiveOptions();
            var result = await _runner.RunSearchAsync(request, activeOptions, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(result.StandardError)
                    ? $"yt-dlp exited with code {result.ExitCode}."
                    : result.StandardError.Trim();
                return SearchResultPage.Failed($"Search failed: {error}");
            }

            var videos = ParseVideos(result.StandardOutput)
                .Where(video => !video.IsShort)
                .Take(activeOptions.MaxResults)
                .ToList();

            return videos.Count == 0
                ? new SearchResultPage(videos, "No video results found.")
                : new SearchResultPage(videos,
                    $"Found {videos.Count} video result{(videos.Count == 1 ? string.Empty : "s")}.");
        }
        catch (Win32Exception)
        {
            return SearchResultPage.Failed("Search failed: yt-dlp is not installed.");
        }
        catch (JsonException ex)
        {
            return SearchResultPage.Failed($"Search failed: yt-dlp returned invalid JSON ({ex.Message}).");
        }
        catch (TimeoutException ex)
        {
            return SearchResultPage.Failed($"Search failed: {ex.Message}");
        }
    }

    public bool IsLikelyYouTubeUrl(string text)
    {
        return YouTubeUrlParser.Parse(text).Kind is not YouTubeUrlKind.NotYouTube and not YouTubeUrlKind.Invalid;
    }

    private YtDlpOptions GetActiveOptions()
    {
        if (_preferencesService is null) return _staticOptions;
        var prefs = _preferencesService.GetPreferences();
        return _staticOptions with
        {
            ExecutablePath = prefs.YtDlpExecutablePath,
            MaxResults = prefs.MaxResults
        };
    }

    private static IEnumerable<VideoSummary> ParseVideos(string output)
    {
        var trimmedOutput = output.Trim();
        if (trimmedOutput.Length == 0) return [];

        if (trimmedOutput.StartsWith('{'))
            try
            {
                using var document = JsonDocument.Parse(trimmedOutput);
                return ParseRoot(document.RootElement).ToList();
            }
            catch (JsonException) when (trimmedOutput.Contains('\n'))
            {
                // yt-dlp can also emit one JSON object per line depending on flags.
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

    private static IEnumerable<VideoSummary> ParseRoot(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("entries", out var entries)
            && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in entries.EnumerateArray())
            {
                var video = ParseVideo(entry);
                if (video is not null) yield return video;
            }

            yield break;
        }

        var singleVideo = ParseVideo(root);
        if (singleVideo is not null) yield return singleVideo;
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
            GetThumbnailUrl(element),
            IsShort(element, rawUrl),
            canonicalWatchUrl);
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

    private static TimeSpan GetDuration(JsonElement element)
    {
        if (!element.TryGetProperty("duration", out var duration)) return TimeSpan.Zero;

        return duration.ValueKind switch
        {
            JsonValueKind.Number when duration.TryGetDouble(out var seconds) && seconds >= 0 =>
                TimeSpan.FromSeconds(seconds),
            JsonValueKind.String when double.TryParse(duration.GetString(), NumberStyles.Float,
                                          CultureInfo.InvariantCulture, out var seconds) &&
                                      seconds >= 0 => TimeSpan.FromSeconds(seconds),
            _ => TimeSpan.Zero
        };
    }

    private static string GetThumbnailUrl(JsonElement element)
    {
        var directThumbnail = FirstString(element, "thumbnail");
        if (directThumbnail is not null) return directThumbnail;

        if (!element.TryGetProperty("thumbnails", out var thumbnails) || thumbnails.ValueKind != JsonValueKind.Array)
            return string.Empty;

        string? thumbnailUrl = null;
        foreach (var thumbnail in thumbnails.EnumerateArray())
            thumbnailUrl = FirstString(thumbnail, "url") ?? thumbnailUrl;

        return thumbnailUrl ?? string.Empty;
    }

    private static bool IsShort(JsonElement element, string? rawUrl)
    {
        if (element.TryGetProperty("is_short", out var isShort) &&
            isShort.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return isShort.GetBoolean();

        if (rawUrl?.Contains("/shorts/", StringComparison.OrdinalIgnoreCase) == true) return true;

        var title = FirstString(element, "title", "fulltitle");
        return title?.Contains("#shorts", StringComparison.OrdinalIgnoreCase) == true;
    }
}