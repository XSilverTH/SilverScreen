using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Features.Session;
using SilverScreen.Features.Search;

namespace SilverScreen.Infrastructure.YouTube;

public sealed class YtDlpHomeClient : IYouTubeHomeClient
{
    private readonly ISessionService _sessionService;
    private readonly ICookieFileProvider _cookieFileProvider;
    private readonly string _executablePath;
    private readonly TimeSpan _timeout;

    public YtDlpHomeClient(
        ISessionService sessionService,
        ICookieFileProvider cookieFileProvider,
        string executablePath = "yt-dlp",
        TimeSpan? timeout = null)
    {
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _cookieFileProvider = cookieFileProvider ?? throw new ArgumentNullException(nameof(cookieFileProvider));
        _executablePath = executablePath;
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    public async Task<HomeFeedResult> GetHomeFeedAsync(string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(continuationToken))
        {
            return new HomeFeedResult(
                Videos: Array.Empty<VideoSummary>(),
                ContinuationToken: null,
                IsSuccess: true,
                StatusMessage: "Continuations are not supported.",
                RequiresAuthentication: false
            );
        }

        var cookies = _sessionService.GetManualSessionCookies();
        if (cookies is null || string.IsNullOrWhiteSpace(cookies.Content))
        {
            return new HomeFeedResult(
                Videos: Array.Empty<VideoSummary>(),
                ContinuationToken: null,
                IsSuccess: false,
                StatusMessage: "Authentication session not found.",
                RequiresAuthentication: true
            );
        }

        using var cookieFile = _cookieFileProvider.CreateCookieFile();
        if (cookieFile is null || string.IsNullOrWhiteSpace(cookieFile.Path))
        {
            return new HomeFeedResult(
                Videos: Array.Empty<VideoSummary>(),
                ContinuationToken: null,
                IsSuccess: false,
                StatusMessage: "Failed to create temporary cookie lease.",
                RequiresAuthentication: true
            );
        }

        var (firstResult, firstVideos) =
            await ExecuteYtDlpAsync(cookieFile.Path, cancellationToken).ConfigureAwait(false);

        if (!firstResult.IsSuccess)
        {
            return firstResult;
        }

        if (firstVideos.Count > 0)
        {
            return firstResult;
        }

        // Dispose early so no cookie lease survives.
        cookieFile.Dispose();

        var (retryResult, retryVideos) = await ExecuteYtDlpAsync(null, cancellationToken).ConfigureAwait(false);

        if (!retryResult.IsSuccess)
        {
            return retryResult;
        }

        return new HomeFeedResult(
            Videos: retryVideos,
            ContinuationToken: null,
            IsSuccess: true,
            StatusMessage: "Public recommendations are displayed.",
            RequiresAuthentication: false
        );
    }

    private async Task<(HomeFeedResult Result, IReadOnlyList<VideoSummary> Videos)> ExecuteYtDlpAsync(
        string? cookieFilePath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("--dump-single-json");
        startInfo.ArgumentList.Add("--flat-playlist");
        startInfo.ArgumentList.Add("--skip-download");
        if (cookieFilePath is not null)
        {
            startInfo.ArgumentList.Add("--cookies");
            startInfo.ArgumentList.Add(cookieFilePath);
        }

        startInfo.ArgumentList.Add(":ytrec");

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                return (new HomeFeedResult(
                    Videos: Array.Empty<VideoSummary>(),
                    ContinuationToken: null,
                    IsSuccess: false,
                    StatusMessage: "Failed to start yt-dlp process.",
                    RequiresAuthentication: false
                ), Array.Empty<VideoSummary>());
            }
        }
        catch (Exception)
        {
            return (new HomeFeedResult(
                Videos: Array.Empty<VideoSummary>(),
                ContinuationToken: null,
                IsSuccess: false,
                StatusMessage: "Exception while starting yt-dlp process.",
                RequiresAuthentication: false
            ), Array.Empty<VideoSummary>());
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
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

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return (new HomeFeedResult(
                Videos: Array.Empty<VideoSummary>(),
                ContinuationToken: null,
                IsSuccess: false,
                StatusMessage: "yt-dlp process execution timed out.",
                RequiresAuthentication: false
            ), Array.Empty<VideoSummary>());
        }
        catch (Exception)
        {
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
                Videos: Array.Empty<VideoSummary>(),
                ContinuationToken: null,
                IsSuccess: false,
                StatusMessage: "Exception while executing yt-dlp process.",
                RequiresAuthentication: false
            ), Array.Empty<VideoSummary>());
        }

        if (process.ExitCode != 0)
        {
            try
            {
                await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }

            return (new HomeFeedResult(
                Videos: Array.Empty<VideoSummary>(),
                ContinuationToken: null,
                IsSuccess: false,
                StatusMessage: $"yt-dlp process exited with error code {process.ExitCode}.",
                RequiresAuthentication: false
            ), Array.Empty<VideoSummary>());
        }

        string output;
        try
        {
            output = await outputTask.ConfigureAwait(false);
            _ = await errorTask.ConfigureAwait(false);
        }
        catch (Exception)
        {
            return (new HomeFeedResult(
                Videos: Array.Empty<VideoSummary>(),
                ContinuationToken: null,
                IsSuccess: false,
                StatusMessage: "Failed to read output from yt-dlp process.",
                RequiresAuthentication: false
            ), Array.Empty<VideoSummary>());
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            return (new HomeFeedResult(
                Videos: Array.Empty<VideoSummary>(),
                ContinuationToken: null,
                IsSuccess: false,
                StatusMessage: "yt-dlp process returned empty output.",
                RequiresAuthentication: false
            ), Array.Empty<VideoSummary>());
        }

        try
        {
            var videos = ParsePlaylistOutput(output);
            return (new HomeFeedResult(
                Videos: videos,
                ContinuationToken: null,
                IsSuccess: true,
                StatusMessage: "Recommendations loaded successfully.",
                RequiresAuthentication: false
            ), videos);
        }
        catch (Exception)
        {
            return (new HomeFeedResult(
                Videos: Array.Empty<VideoSummary>(),
                ContinuationToken: null,
                IsSuccess: false,
                StatusMessage: "Failed to parse yt-dlp recommendation output.",
                RequiresAuthentication: false
            ), Array.Empty<VideoSummary>());
        }
    }

    private static IReadOnlyList<VideoSummary> ParsePlaylistOutput(string output)
    {
        var videos = new List<VideoSummary>();
        var trimmed = output.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return videos;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("entries", out var entries) &&
                entries.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in entries.EnumerateArray())
                {
                    var video = ParseEntry(entry);
                    if (video is not null)
                    {
                        videos.Add(video);
                    }
                }
            }
        }
        catch (JsonException)
        {
            foreach (var line in trimmed.Split('\n',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var video = ParseEntry(doc.RootElement);
                    if (video is not null)
                    {
                        videos.Add(video);
                    }
                }
                catch (JsonException)
                {
                }
            }
        }

        return videos;
    }


    private static VideoSummary? ParseEntry(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = FirstString(element, "id", "display_id");
        var title = FirstString(element, "title", "fulltitle");

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var rawUrl = FirstString(element, "webpage_url", "original_url", "url");

        if (IsShort(element, rawUrl))
        {
            return null;
        }

        var parsedUrl = YouTubeUrlParser.Parse(rawUrl);
        var canonicalWatchUrl = PlaybackRequest.LooksLikeYouTubeVideoId(id)
            ? PlaybackRequest.BuildWatchUrl(id)
            : (parsedUrl.CanonicalWatchUrl ?? rawUrl);

        var channel = FirstString(element, "channel", "uploader") ?? "YouTube";
        var duration = GetDuration(element);
        var thumbnailUrl = GetHighestQualityThumbnailUrl(element);

        return new VideoSummary(
            Id: id,
            Title: title,
            ChannelName: channel,
            Duration: duration,
            ThumbnailUrl: thumbnailUrl,
            IsShort: false,
            WatchUrl: canonicalWatchUrl
        );
    }

    private static bool IsShort(JsonElement element, string? rawUrl)
    {
        if (element.TryGetProperty("is_short", out var isShortProp) &&
            (isShortProp.ValueKind == JsonValueKind.True || isShortProp.ValueKind == JsonValueKind.False))
        {
            return isShortProp.GetBoolean();
        }

        if (!string.IsNullOrWhiteSpace(rawUrl))
        {
            if (rawUrl.Contains("/shorts/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var parsedUrl = YouTubeUrlParser.Parse(rawUrl);
            if (parsedUrl.Kind == YouTubeUrlKind.Shorts)
            {
                return true;
            }
        }

        var title = FirstString(element, "title", "fulltitle");
        if (title is not null && title.Contains("#shorts", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string? FirstString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out var property) &&
                property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static TimeSpan GetDuration(JsonElement element)
    {
        if (element.TryGetProperty("duration", out var duration))
        {
            double seconds = 0;
            if (duration.ValueKind == JsonValueKind.Number && duration.TryGetDouble(out var s))
            {
                seconds = s;
            }
            else if (duration.ValueKind == JsonValueKind.String && double.TryParse(duration.GetString(),
                         System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                         out var s2))
            {
                seconds = s2;
            }

            if (seconds > 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }

        return TimeSpan.Zero;
    }

    private static string GetHighestQualityThumbnailUrl(JsonElement element)
    {
        if (element.TryGetProperty("thumbnails", out var thumbnails) && thumbnails.ValueKind == JsonValueKind.Array)
        {
            string? bestUrl = null;
            double bestArea = -1;
            int bestPreference = int.MinValue;

            foreach (var thumbnail in thumbnails.EnumerateArray())
            {
                var url = FirstString(thumbnail, "url");
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                int preference = int.MinValue;
                if (thumbnail.TryGetProperty("preference", out var prefProp) &&
                    prefProp.ValueKind == JsonValueKind.Number &&
                    prefProp.TryGetInt32(out var p))
                {
                    preference = p;
                }

                double width = -1;
                double height = -1;
                if (thumbnail.TryGetProperty("width", out var wProp) && wProp.ValueKind == JsonValueKind.Number)
                {
                    wProp.TryGetDouble(out width);
                }

                if (thumbnail.TryGetProperty("height", out var hProp) && hProp.ValueKind == JsonValueKind.Number)
                {
                    hProp.TryGetDouble(out height);
                }

                double area = (width > 0 && height > 0) ? (width * height) : -1;

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
                        if (area > bestArea)
                        {
                            bestUrl = url;
                            bestArea = area;
                        }
                    }
                }
            }

            if (bestUrl != null)
            {
                return bestUrl;
            }
        }

        return FirstString(element, "thumbnail") ?? string.Empty;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Exception)
        {
        }
    }
}