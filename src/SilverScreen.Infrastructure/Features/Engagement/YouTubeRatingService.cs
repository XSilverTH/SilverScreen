using System.Globalization;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Infrastructure.Features.Engagement;

public sealed class YouTubeRatingService : IYouTubeRatingService, IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly Regex LikeStatusRegex = new(
        """\\?"likeStatus\\?"\s*:\s*\\?"(LIKE|DISLIKE|INDIFFERENT)\\?""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private static readonly Regex LikeParamsRegex = CreateParameterRegex("likeParams");
    private static readonly Regex DislikeParamsRegex = CreateParameterRegex("dislikeParams");
    private static readonly Regex RemoveLikeParamsRegex = CreateParameterRegex("removeLikeParams");
    private readonly SemaphoreSlim _bootstrapLock = new(1, 1);
    private readonly bool _disposeHttpClient;
    private readonly HttpClient _httpClient;
    private readonly YouTubeHomeClientOptions _options;
    private readonly ISessionService _sessionService;
    private YouTubeBootstrapConfig? _bootstrapConfig;
    private readonly ConcurrentDictionary<string, RatingMetadata> _ratingMetadataByVideoId = new(StringComparer.Ordinal);

    public YouTubeRatingService(ISessionService sessionService)
        : this(CreateDefaultHttpClient(), sessionService, disposeHttpClient: true)
    {
    }

    public YouTubeRatingService(HttpClient httpClient, ISessionService sessionService,
        YouTubeHomeClientOptions? options = null, bool disposeHttpClient = false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(sessionService);
        _httpClient = httpClient;
        _sessionService = sessionService;
        _options = options ?? new YouTubeHomeClientOptions();
        _disposeHttpClient = disposeHttpClient;
    }

    public void Dispose()
    {
        _bootstrapLock.Dispose();
        if (_disposeHttpClient) _httpClient.Dispose();
    }

    public async Task<YouTubeRatingState> GetRatingStateAsync(string videoId,
        CancellationToken cancellationToken = default)
    {
        if (!PlaybackRequest.LooksLikeYouTubeVideoId(videoId)) return YouTubeRatingState.None;

        var metadata = await LoadRatingMetadataAsync(videoId, cancellationToken).ConfigureAwait(false);
        return metadata?.State ?? YouTubeRatingState.None;
    }

    public Task<bool> SubmitVoteAsync(string videoId, VideoVote vote, CancellationToken cancellationToken = default)
    {
        return ExecuteActionAsync(videoId, vote, vote == VideoVote.Like ? "like" : "dislike", cancellationToken);
    }

    public Task<bool> RemoveVoteAsync(string videoId, VideoVote vote, CancellationToken cancellationToken = default)
    {
        return ExecuteActionAsync(videoId, vote, "removelike", cancellationToken);
    }

    private async Task<bool> ExecuteActionAsync(string videoId, VideoVote vote, string action,
        CancellationToken cancellationToken)
    {
        if (!PlaybackRequest.LooksLikeYouTubeVideoId(videoId) || vote is not (VideoVote.Like or VideoVote.Dislike))
            return false;

        var metadata = _ratingMetadataByVideoId.TryGetValue(videoId, out var cached)
            ? cached
            : await LoadRatingMetadataAsync(videoId, cancellationToken).ConfigureAwait(false);
        if (metadata is null) return false;

        try
        {
            var authenticatedRequest = await CreateAuthenticatedRequestAsync($"like/{action}", videoId,
                context => new RatingRequestPayload
                {
                    Context = context,
                    Target = new RatingTarget { VideoId = videoId },
                    Params = ActionParams(action, metadata)
                },
                YouTubeRequestJsonContext.Default.RatingRequestPayload, cancellationToken).ConfigureAwait(false);
            if (authenticatedRequest is null) return false;

            using var request = authenticatedRequest.Request;
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) _ratingMetadataByVideoId.TryRemove(videoId, out _);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<RatingMetadata?> LoadRatingMetadataAsync(string videoId, CancellationToken cancellationToken)
    {
        var credentials = GetCredentials();
        if (credentials is null) return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, PlaybackRequest.BuildWatchUrl(videoId));
            request.Headers.UserAgent.ParseAdd(YouTubeHomeClientOptions.UserAgent);
            request.Headers.Add("Origin", YouTubeHomeClientOptions.Origin);
            request.Headers.Add("Referer", YouTubeHomeClientOptions.Referer);
            request.Headers.Add("Cookie", credentials.CookieHeader);
            request.Headers.Add("X-Goog-AuthUser", (_options.AuthUser ?? 0).ToString(CultureInfo.InvariantCulture));
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var metadata = new RatingMetadata(
                LikeStatusRegex.Match(html).Groups[1].Value switch
                {
                    "LIKE" => YouTubeRatingState.Like,
                    "DISLIKE" => YouTubeRatingState.Dislike,
                    _ => YouTubeRatingState.None
                },
                LikeParamsRegex.Match(html).Groups[1].Value,
                DislikeParamsRegex.Match(html).Groups[1].Value,
                RemoveLikeParamsRegex.Match(html).Groups[1].Value);
            _ratingMetadataByVideoId[videoId] = metadata;
            return metadata;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? ActionParams(string action, RatingMetadata metadata)
    {
        return action switch
        {
            "like" => metadata.LikeParams,
            "dislike" => metadata.DislikeParams,
            "removelike" => metadata.RemoveLikeParams,
            _ => null
        };
    }

    private async Task<AuthenticatedRequest<T>?> CreateAuthenticatedRequestAsync<T>(string endpoint, string videoId,
        Func<BrowseRequestContext, T> createPayload,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> payloadTypeInfo,
        CancellationToken cancellationToken) where T : class
    {
        var credentials = GetCredentials();
        if (credentials is null) return null;

        var config = await EnsureBootstrappedAsync(credentials, cancellationToken).ConfigureAwait(false);
        if (config is null) return null;

        var payload = createPayload(CreateContext(videoId, config));

        var requestUrl = $"https://www.youtube.com/youtubei/v1/{endpoint}?key={Uri.EscapeDataString(config.ApiKey)}&prettyPrint=false";
        var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, payloadTypeInfo), Encoding.UTF8, "application/json")
        };
        AddAuthenticatedHeaders(request, credentials, config);
        return new AuthenticatedRequest<T>(request);
    }

    private YouTubeCredentials? GetCredentials()
    {
        var cookies = _sessionService.GetManualSessionCookies();
        return cookies?.Format == SessionCookieFormat.NetscapeCookiesText
            ? YouTubeCredentials.ParseNetscape(cookies.Content)
            : null;
    }
    private BrowseRequestContext CreateContext(string videoId, YouTubeBootstrapConfig config)
    {
        return new BrowseRequestContext
        {
            Client = new BrowseRequestClientContext
            {
                ClientName = "WEB",
                ClientVersion = config.ClientVersion,
                OriginalUrl = PlaybackRequest.BuildWatchUrl(videoId)!,
                Hl = "en",
                Gl = "US",
                VisitorData = config.VisitorData
            },
            User = new BrowseRequestUserContext
            {
                LockedSafetyMode = false,
                Authuser = _options.AuthUser
            }
        };
    }

    private async Task<YouTubeBootstrapConfig?> EnsureBootstrappedAsync(YouTubeCredentials credentials,
        CancellationToken cancellationToken)
    {
        if (_bootstrapConfig is not null) return _bootstrapConfig;

        await _bootstrapLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_bootstrapConfig is not null) return _bootstrapConfig;

            using var request = new HttpRequestMessage(HttpMethod.Get, YouTubeHomeClientOptions.Referer);
            request.Headers.UserAgent.ParseAdd(YouTubeHomeClientOptions.UserAgent);
            request.Headers.Add("Origin", YouTubeHomeClientOptions.Origin);
            request.Headers.Add("Referer", YouTubeHomeClientOptions.Referer);
            request.Headers.Add("Cookie", credentials.CookieHeader);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _bootstrapConfig = YouTubeConfigBootstrap.Extract(html);
            return _bootstrapConfig;
        }
        finally
        {
            _bootstrapLock.Release();
        }
    }

    private void AddAuthenticatedHeaders(HttpRequestMessage request, YouTubeCredentials credentials,
        YouTubeBootstrapConfig config)
    {
        request.Headers.UserAgent.ParseAdd(YouTubeHomeClientOptions.UserAgent);
        request.Headers.Add("Origin", YouTubeHomeClientOptions.Origin);
        request.Headers.Add("Referer", YouTubeHomeClientOptions.Referer);
        request.Headers.Add("X-Origin", YouTubeHomeClientOptions.Origin);
        request.Headers.Add("Cookie", credentials.CookieHeader);
        request.Headers.Add("X-Youtube-Client-Name", "1");
        request.Headers.Add("X-Youtube-Client-Version", config.ClientVersion);
        if (!string.IsNullOrEmpty(config.VisitorData)) request.Headers.Add("X-Goog-Visitor-Id", config.VisitorData);
        request.Headers.Add("X-Goog-AuthUser", (_options.AuthUser ?? 0).ToString(CultureInfo.InvariantCulture));
        request.Headers.Add("X-Youtube-Bootstrap-Logged-In", "true");

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        request.Headers.Authorization = new AuthenticationHeaderValue("SAPISIDHASH",
            $"{timestamp}_{credentials.GenerateSapisidHash(timestamp)}");
    }

    private static Regex CreateParameterRegex(string propertyName)
    {
        return new Regex($"\\\\?\"{Regex.Escape(propertyName)}\\\\?\"\\s*:\\s*\\\\?\"([^\"\\\\]+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }


    private static HttpClient CreateDefaultHttpClient()
    {
        return new HttpClient { Timeout = DefaultTimeout };
    }

    private sealed record RatingMetadata(YouTubeRatingState State, string LikeParams, string DislikeParams,
        string RemoveLikeParams);

    private sealed record AuthenticatedRequest<T>(HttpRequestMessage Request) where T : class;
}
