using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.YouTube;

public sealed class YouTubeHomeClient(
    HttpClient httpClient,
    ISessionService sessionService,
    YouTubeHomeClientOptions? options = null)
    : IYouTubeHomeClient, IDisposable
{
    private readonly SemaphoreSlim _bootstrapLock = new(1, 1);
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    private readonly YouTubeHomeClientOptions _options = options ?? new YouTubeHomeClientOptions();

    private readonly ISessionService _sessionService =
        sessionService ?? throw new ArgumentNullException(nameof(sessionService));

    private YouTubeBootstrapConfig? _bootstrapConfig;

    public Func<long> TimeSource { get; set; } = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public void Dispose()
    {
        _bootstrapLock.Dispose();
    }

    public async Task<HomeFeedResult> GetHomeFeedAsync(string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Get Session Cookies
        var sessionCookies = _sessionService.GetManualSessionCookies();
        if (sessionCookies == null || string.IsNullOrWhiteSpace(sessionCookies.Content))
            return new HomeFeedResult(
                [],
                null,
                false,
                "Authentication session not found.",
                true
            );

        // 2. Parse Cookies
        var credentials = YouTubeCredentials.ParseNetscape(sessionCookies.Content);
        if (credentials == null)
            return new HomeFeedResult(
                [],
                null,
                false,
                "Incomplete authentication credentials. Missing required session cookies.",
                true
            );

        // 3. Ensure bootstrap config
        YouTubeBootstrapConfig? config;
        try
        {
            config = await EnsureBootstrappedAsync(credentials, cancellationToken);
        }
        catch (Exception)
        {
            return new HomeFeedResult(
                [],
                null,
                false,
                "Failed to load YouTube bootstrap configuration due to connection issues.",
                false
            );
        }

        if (config == null)
            return new HomeFeedResult(
                [],
                null,
                false,
                "Failed to extract YouTube client configuration from homepage.",
                false
            );

        // 4. Build JSON request payload
        var clientContext = new Dictionary<string, object>
        {
            ["clientName"] = "WEB",
            ["clientVersion"] = config.ClientVersion,
            ["originalUrl"] = "https://www.youtube.com/",
            ["hl"] = "en",
            ["gl"] = "US"
        };

        if (!string.IsNullOrEmpty(config.VisitorData))
            clientContext["visitorData"] = config.VisitorData;

        var userContext = new Dictionary<string, object>
        {
            ["lockedSafetyMode"] = false
        };

        if (_options.AuthUser.HasValue)
            userContext["authuser"] = _options.AuthUser.Value;

        var context = new Dictionary<string, object>
        {
            ["client"] = clientContext,
            ["user"] = userContext
        };

        var payload = new Dictionary<string, object>
        {
            ["context"] = context
        };

        if (string.IsNullOrEmpty(continuationToken))
            payload["browseId"] = "FEwhat_to_watch";
        else
            payload["continuation"] = continuationToken;

        string jsonPayload;
        try
        {
            jsonPayload = JsonSerializer.Serialize(payload);
        }
        catch (Exception)
        {
            return new HomeFeedResult(
                [],
                null,
                false,
                "Failed to serialize YouTube request payload.",
                false
            );
        }

        // 5. Send POST request to InnerTube API
        var requestUrl =
            $"https://www.youtube.com/youtubei/v1/browse?key={Uri.EscapeDataString(config.ApiKey)}&prettyPrint=false";
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);

        request.Headers.UserAgent.ParseAdd(_options.UserAgent);
        request.Headers.Add("Origin", _options.Origin);
        request.Headers.Add("Referer", _options.Referer);
        request.Headers.Add("X-Origin", _options.Origin);
        request.Headers.Add("Cookie", credentials.CookieHeader);
        request.Headers.Add("X-Youtube-Client-Name", "1");
        request.Headers.Add("X-Youtube-Client-Version", config.ClientVersion);
        if (!string.IsNullOrEmpty(config.VisitorData))
            request.Headers.Add("X-Goog-Visitor-Id", config.VisitorData);

        if (_options.AuthUser.HasValue)
            request.Headers.Add("X-Goog-AuthUser",
                _options.AuthUser.Value.ToString(CultureInfo.InvariantCulture));

        // Derive SAPISIDHASH
        var timestamp = TimeSource();
        var sapisidHash = credentials.GenerateSapisidHash(timestamp);
        request.Headers.Authorization = new AuthenticationHeaderValue("SAPISIDHASH", $"{timestamp}_{sapisidHash}");

        request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception)
        {
            return new HomeFeedResult(
                [],
                null,
                false,
                "Network error occurred while calling YouTube InnerTube API.",
                false
            );
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var isAuthError = response.StatusCode == HttpStatusCode.Unauthorized ||
                                  response.StatusCode == HttpStatusCode.Forbidden;
                return new HomeFeedResult(
                    [],
                    null,
                    false,
                    $"YouTube InnerTube API returned HTTP status {(int)response.StatusCode}.",
                    isAuthError
                );
            }

            string jsonResponse;
            try
            {
                jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception)
            {
                return new HomeFeedResult(
                    [],
                    null,
                    false,
                    "Failed to read response content from YouTube API.",
                    false
                );
            }

            try
            {
                using var document = JsonDocument.Parse(jsonResponse);
                var videos = new List<VideoSummary>();
                string? nextContinuationToken = null;

                WalkJson(document.RootElement, videos, ref nextContinuationToken);

                return new HomeFeedResult(
                    videos,
                    nextContinuationToken,
                    true,
                    null,
                    false
                );
            }
            catch (Exception)
            {
                return new HomeFeedResult(
                    [],
                    null,
                    false,
                    "Failed to parse the YouTube home feed JSON response.",
                    false
                );
            }
        }
    }

    private async Task<YouTubeBootstrapConfig?> EnsureBootstrappedAsync(YouTubeCredentials credentials,
        CancellationToken cancellationToken)
    {
        if (_bootstrapConfig != null)
            return _bootstrapConfig;

        await _bootstrapLock.WaitAsync(cancellationToken);
        try
        {
            if (_bootstrapConfig != null)
                return _bootstrapConfig;

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.youtube.com/");
            request.Headers.UserAgent.ParseAdd(_options.UserAgent);
            request.Headers.Add("Origin", _options.Origin);
            request.Headers.Add("Referer", _options.Referer);
            request.Headers.Add("Cookie", credentials.CookieHeader);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var config = YouTubeConfigBootstrap.Extract(html);
            if (config != null)
                _bootstrapConfig = config;

            return _bootstrapConfig;
        }
        finally
        {
            _bootstrapLock.Release();
        }
    }


    private static void WalkJson(JsonElement element, List<VideoSummary> videos, ref string? continuationToken)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                foreach (var property in element.EnumerateObject())
                {
                    var name = property.Name;

                    // Filter promoted/ad renderer keys
                    if (name.Equals("adSlotRenderer", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("adPlacementRenderer", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("promotedVideoRenderer", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("promotedSparklesTextSearchGridRenderer", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("promotedSparklesWebRenderer", StringComparison.OrdinalIgnoreCase) ||
                        (name.StartsWith("ad", StringComparison.OrdinalIgnoreCase) &&
                         name.EndsWith("Renderer", StringComparison.OrdinalIgnoreCase)))
                        continue;

                    // Filter shortsLockupViewModel/reel endpoints
                    if (name.Equals("shortsLockupViewModel", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("reelEndpoint", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (name.Equals("videoRenderer", StringComparison.OrdinalIgnoreCase))
                    {
                        var video = ParseVideoRenderer(property.Value);
                        if (video != null)
                            videos.Add(video);

                        continue;
                    }

                    if (name.Equals("continuationCommand", StringComparison.OrdinalIgnoreCase))
                    {
                        if (property.Value.TryGetProperty("token", out var tokenProp) &&
                            tokenProp.ValueKind == JsonValueKind.String)
                            continuationToken = tokenProp.GetString();

                        continue;
                    }

                    WalkJson(property.Value, videos, ref continuationToken);
                }

                break;
            }
            case JsonValueKind.Array:
            {
                foreach (var item in element.EnumerateArray())
                    WalkJson(item, videos, ref continuationToken);

                break;
            }
        }
    }

    private static VideoSummary? ParseVideoRenderer(JsonElement renderer)
    {
        if (HasReelEndpoint(renderer))
            return null;

        if (!renderer.TryGetProperty("videoId", out var videoIdProp) || videoIdProp.ValueKind != JsonValueKind.String)
            return null;

        var videoId = videoIdProp.GetString();
        if (string.IsNullOrEmpty(videoId))
            return null;

        var title = "";
        if (renderer.TryGetProperty("title", out var titleProp))
            title = ExtractText(titleProp) ?? "";

        var channelName = "";
        if (renderer.TryGetProperty("ownerText", out var ownerProp))
            channelName = ExtractText(ownerProp) ?? "";

        if (string.IsNullOrEmpty(channelName) && renderer.TryGetProperty("shortBylineText", out var shortProp))
            channelName = ExtractText(shortProp) ?? "";

        if (string.IsNullOrEmpty(channelName) && renderer.TryGetProperty("longBylineText", out var longProp))
            channelName = ExtractText(longProp) ?? "";

        var duration = TimeSpan.Zero;
        if (renderer.TryGetProperty("lengthText", out var lengthProp))
        {
            var durationStr = ExtractText(lengthProp);
            if (!string.IsNullOrEmpty(durationStr))
                duration = ParseDuration(durationStr);
        }

        var thumbnailUrl = "";
        if (renderer.TryGetProperty("thumbnail", out var thumbProp) &&
            thumbProp.TryGetProperty("thumbnails", out var listProp) &&
            listProp.ValueKind == JsonValueKind.Array)
        {
            var count = listProp.GetArrayLength();
            if (count > 0)
            {
                var lastThumb = listProp[count - 1];
                if (lastThumb.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String)
                    thumbnailUrl = urlProp.GetString() ?? "";
            }
        }

        var watchUrl = $"https://www.youtube.com/watch?v={videoId}";

        return new VideoSummary(
            videoId,
            title,
            channelName,
            duration,
            thumbnailUrl,
            false,
            watchUrl
        );
    }

    private static bool HasReelEndpoint(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Name.Equals("reelEndpoint", StringComparison.OrdinalIgnoreCase))
                        return true;

                    if (HasReelEndpoint(prop.Value))
                        return true;
                }

                break;
            }
            case JsonValueKind.Array:
            {
                if (element.EnumerateArray().Any(HasReelEndpoint))
                    return true;

                break;
            }
        }

        return false;
    }

    private static string? ExtractText(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Object when element.TryGetProperty("simpleText", out var simpleProp) &&
                                           simpleProp.ValueKind == JsonValueKind.String:
                return simpleProp.GetString();
            case JsonValueKind.Object when element.TryGetProperty("runs", out var runsProp) &&
                                           runsProp.ValueKind == JsonValueKind.Array:
            {
                var sb = new StringBuilder();
                foreach (var run in runsProp.EnumerateArray())
                    if (run.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                        sb.Append(textProp.GetString());

                return sb.ToString();
            }
            case JsonValueKind.Undefined:
            case JsonValueKind.Array:
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
            default:
                return null;
        }
    }

    private static TimeSpan ParseDuration(string durationStr)
    {
        if (string.IsNullOrWhiteSpace(durationStr))
            return TimeSpan.Zero;

        var parts = durationStr.Split(':');
        try
        {
            switch (parts.Length)
            {
                case 1 when int.TryParse(parts[0], out var secs):
                    return TimeSpan.FromSeconds(secs);
                case 2 when int.TryParse(parts[0], out var mins) && int.TryParse(parts[1], out var secs):
                    return new TimeSpan(0, mins, secs);
                case 3 when int.TryParse(parts[0], out var hours) && int.TryParse(parts[1], out var mins) &&
                            int.TryParse(parts[2], out var secs):
                    return new TimeSpan(hours, mins, secs);
            }
        }
        catch
        {
            // Ignore format exceptions
        }

        return TimeSpan.Zero;
    }
}