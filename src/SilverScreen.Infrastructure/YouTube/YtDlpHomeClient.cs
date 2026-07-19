using Serilog;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Search;
using SilverScreen.Infrastructure.Features.Session;

namespace SilverScreen.Infrastructure.YouTube;

public sealed class YtDlpHomeClient(
    ISessionService sessionService,
    ICookieFileProvider cookieFileProvider,
    string executablePath = "yt-dlp",
    TimeSpan? timeout = null)
    : IYouTubeHomeClient
{
    private static readonly ILogger Logger = Log.ForContext<YtDlpHomeClient>();
    private readonly ICookieFileProvider _cookieFileProvider =
        cookieFileProvider ?? throw new ArgumentNullException(nameof(cookieFileProvider));

    private readonly ISessionService _sessionService =
        sessionService ?? throw new ArgumentNullException(nameof(sessionService));

    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(30);

    public async Task<HomeFeedResult> GetHomeFeedAsync(string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(continuationToken))
            return new HomeFeedResult(
                Array.Empty<VideoSummary>(),
                null,
                true,
                "Continuations are not supported.",
                false
            );

        var cookies = _sessionService.GetManualSessionCookies();
        if (cookies is null || string.IsNullOrWhiteSpace(cookies.Content))
            return new HomeFeedResult(
                Array.Empty<VideoSummary>(),
                null,
                false,
                "Authentication session not found.",
                true
            );

        using var cookieFile = _cookieFileProvider.CreateCookieFile();
        if (cookieFile is null || string.IsNullOrWhiteSpace(cookieFile.Path))
            return new HomeFeedResult(
                Array.Empty<VideoSummary>(),
                null,
                false,
                "Failed to create temporary cookie lease.",
                true
            );

        var (firstResult, firstVideos) =
            await ExecuteYtDlpAsync(cookieFile.Path, cancellationToken).ConfigureAwait(false);

        if (!firstResult.IsSuccess || firstVideos.Count > 0)
            return firstResult;

        var (retryResult, retryVideos) = await ExecuteYtDlpAsync(null, cancellationToken).ConfigureAwait(false);

        if (!retryResult.IsSuccess)
            return retryResult;

        return new HomeFeedResult(
            retryVideos,
            null,
            true,
            "Public recommendations are displayed.",
            false
        );
    }

    private async Task<(HomeFeedResult Result, IReadOnlyList<VideoSummary> Videos)> ExecuteYtDlpAsync(
        string? cookieFilePath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("--dump-single-json");
        startInfo.ArgumentList.Add("--flat-playlist");
        startInfo.ArgumentList.Add("--skip-download");
        startInfo.ArgumentList.Add("--extractor-args");
        startInfo.ArgumentList.Add("youtubetab:approximate_date");
        if (cookieFilePath is not null)
        {
            startInfo.ArgumentList.Add("--cookies");
            startInfo.ArgumentList.Add(cookieFilePath);
        }

        startInfo.ArgumentList.Add(":ytrec");

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);

        using var process = new Process();
        process.StartInfo = startInfo;

        try
        {
            if (!process.Start())
            {
                Logger.Warning("yt-dlp returned no process for home recommendations");
                return (new HomeFeedResult(
                    Array.Empty<VideoSummary>(),
                    null,
                    false,
                    "Failed to start yt-dlp process.",
                    false
                ), Array.Empty<VideoSummary>());
            }
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Could not start yt-dlp for home recommendations");
            return (new HomeFeedResult(
                Array.Empty<VideoSummary>(),
                null,
                false,
                "Exception while starting yt-dlp process.",
                false
            ), Array.Empty<VideoSummary>());
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            TryKill(process);
            try
            {
                await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            }
            catch
            {
                // ignore standard stream read exceptions on cancel/kill
            }

            if (cancellationToken.IsCancellationRequested) throw;
            Logger.Warning(exception, "yt-dlp timed out while loading home recommendations");

            return (new HomeFeedResult(
                [],
                null,
                false,
                "yt-dlp process execution timed out.",
                false
            ), []);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "yt-dlp failed while loading home recommendations");
            TryKill(process);
            try
            {
                await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            }
            catch
            {
                // ignore standard stream read exceptions on exception/kill
            }

            return (new HomeFeedResult(
                [],
                null,
                false,
                "Exception while executing yt-dlp process.",
                false
            ), []);
        }

        if (process.ExitCode != 0)
        {
            Logger.Warning(
                "yt-dlp exited with code {ExitCode} while loading home recommendations",
                process.ExitCode);
            try
            {
                await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }

            return (new HomeFeedResult(
                [],
                null,
                false,
                $"yt-dlp process exited with error code {process.ExitCode}.",
                false
            ), []);
        }

        string output;
        try
        {
            output = await outputTask.ConfigureAwait(false);
            _ = await errorTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Could not read yt-dlp output for home recommendations");
            return (new HomeFeedResult(
                [],
                null,
                false,
                "Failed to read output from yt-dlp process.",
                false
            ), []);
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            Logger.Warning("yt-dlp returned empty output for home recommendations");
            return (new HomeFeedResult(
                [],
                null,
                false,
                "yt-dlp process returned empty output.",
                false
            ), []);
        }
        try
        {
            var videos = ParsePlaylistOutput(output);
            return (new HomeFeedResult(
                videos,
                null,
                true,
                "Recommendations loaded successfully.",
                false
            ), videos);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Could not parse yt-dlp output for home recommendations");
            return (new HomeFeedResult(
                [],
                null,
                false,
                "Failed to parse yt-dlp recommendation output.",
                false
            ), []);
        }
    }

    private static IReadOnlyList<VideoSummary> ParsePlaylistOutput(string output)
    {
        var videos = new List<VideoSummary>();
        var trimmed = output.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return videos;

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("entries", out var entries) &&
                entries.ValueKind == JsonValueKind.Array)
                videos.AddRange(entries.EnumerateArray().Select(ParseEntry).OfType<VideoSummary>());
        }
        catch (JsonException)
        {
            foreach (var line in trimmed.Split('\n',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var video = ParseEntry(doc.RootElement);
                    if (video is not null) videos.Add(video);
                }
                catch (JsonException)
                {
                }
        }

        return videos;
    }


    private static VideoSummary? ParseEntry(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var id = FirstString(element, "id", "display_id");
        var title = FirstString(element, "title", "fulltitle");

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
            return null;

        var rawUrl = FirstString(element, "webpage_url", "original_url", "url");

        if (IsShort(element, rawUrl))
            return null;

        var parsedUrl = YouTubeUrlParser.Parse(rawUrl);
        var canonicalWatchUrl = PlaybackRequest.LooksLikeYouTubeVideoId(id)
            ? PlaybackRequest.BuildWatchUrl(id)
            : parsedUrl.CanonicalWatchUrl ?? rawUrl;

        var channel = FirstString(element, "channel", "uploader") ?? "YouTube";
        var duration = GetDuration(element);
        var thumbnailUrl = GetHighestQualityThumbnailUrl(element);

        return new VideoSummary(
            id,
            title,
            channel,
            duration,
            thumbnailUrl,
            false,
            canonicalWatchUrl,
            GetApproximateUploadDate(element),
            GetPublishedAt(element)
        );
    }

    private static bool IsShort(JsonElement element, string? rawUrl)
    {
        if (element.TryGetProperty("is_short", out var isShortProp) &&
            (isShortProp.ValueKind == JsonValueKind.True || isShortProp.ValueKind == JsonValueKind.False))
            return isShortProp.GetBoolean();

        if (!string.IsNullOrWhiteSpace(rawUrl))
        {
            if (rawUrl.Contains("/shorts/", StringComparison.OrdinalIgnoreCase))
                return true;

            var parsedUrl = YouTubeUrlParser.Parse(rawUrl);
            if (parsedUrl.Kind == YouTubeUrlKind.Shorts)
                return true;
        }

        var title = FirstString(element, "title", "fulltitle");
        return title is not null && title.Contains("#shorts", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var property) ||
                property.ValueKind != JsonValueKind.String) continue;
            var value = property.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static DateOnly? GetApproximateUploadDate(JsonElement element)
    {
        if (element.TryGetProperty("upload_date", out var uploadDate) &&
            uploadDate.ValueKind == JsonValueKind.String &&
            DateOnly.TryParseExact(uploadDate.GetString(), "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsedUploadDate))
            return parsedUploadDate;

        if (!element.TryGetProperty("timestamp", out var timestamp) ||
            timestamp.ValueKind != JsonValueKind.Number ||
            !timestamp.TryGetInt64(out var unixSeconds) ||
            unixSeconds < 0)
            return null;

        try
        {
            return DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static DateTimeOffset? GetPublishedAt(JsonElement element)
    {
        if (!element.TryGetProperty("timestamp", out var timestamp) ||
            timestamp.ValueKind != JsonValueKind.Number ||
            !timestamp.TryGetInt64(out var unixSeconds) ||
            unixSeconds < 0)
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
            JsonValueKind.Number when duration.TryGetDouble(out var s) => s,
            JsonValueKind.String when double.TryParse(duration.GetString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var s2) => s2,
            _ => 0
        };

        return seconds > 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
    }

    private static string GetHighestQualityThumbnailUrl(JsonElement element)
    {
        if (!element.TryGetProperty("thumbnails", out var thumbnails) || thumbnails.ValueKind != JsonValueKind.Array)
            return FirstString(element, "thumbnail") ?? string.Empty;
        string? bestUrl = null;
        double bestArea = -1;
        var bestPreference = int.MinValue;

        foreach (var thumbnail in thumbnails.EnumerateArray())
        {
            var url = FirstString(thumbnail, "url");
            if (string.IsNullOrWhiteSpace(url))
                continue;

            var preference = int.MinValue;
            if (thumbnail.TryGetProperty("preference", out var prefProp) &&
                prefProp.ValueKind == JsonValueKind.Number &&
                prefProp.TryGetInt32(out var p))
                preference = p;

            double width = -1;
            double height = -1;
            if (thumbnail.TryGetProperty("width", out var wProp) && wProp.ValueKind == JsonValueKind.Number)
                wProp.TryGetDouble(out width);

            if (thumbnail.TryGetProperty("height", out var hProp) && hProp.ValueKind == JsonValueKind.Number)
                hProp.TryGetDouble(out height);

            var area = width > 0 && height > 0 ? width * height : -1;

            if (bestUrl == null)
            {
                bestUrl = url;
                bestArea = area;
                bestPreference = preference;
            }
            else
            {
                if (preference > bestPreference)
                {
                    bestUrl = url;
                    bestPreference = preference;
                    bestArea = area;
                }
                else if (preference == bestPreference)
                {
                    if (!(area > bestArea)) continue;
                    bestUrl = url;
                    bestArea = area;
                }
            }
        }

        if (bestUrl != null)
            return bestUrl;

        return FirstString(element, "thumbnail") ?? string.Empty;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (Exception)
        {
            // ignored
        }
    }
}