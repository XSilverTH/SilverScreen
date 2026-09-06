using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Infrastructure.Player;

/// <summary>
/// Fetches dislike estimates from returnyoutubedislike.com. Off by default: unless
/// <see cref="AppPreferences.RydEnabled"/> is true, every call fails closed to null —
/// no HTTP traffic and no cached (possibly stale) entries are served. The per-video
/// cache is bounded to <see cref="MaxCachedVideos"/> entries (oldest-inserted evicted).
/// </summary>
public sealed class ReturnYouTubeDislikeService : IVideoEngagementService, IDisposable
{
    /// <summary>Maximum cached videos; the oldest-inserted entry is evicted past this bound.</summary>
    internal const int MaxCachedVideos = 100;
    private static readonly ILogger Logger = Log.ForContext<ReturnYouTubeDislikeService>();
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);
    private static readonly Uri VotesEndpoint = new("https://returnyoutubedislikeapi.com/votes");
    private readonly bool _disposeHttpClient;
    private readonly ConcurrentDictionary<string, VideoEngagement> _engagementByVideoId = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _insertionOrder = new();
    private readonly HttpClient _httpClient;
    private readonly IPreferencesService? _preferencesService;

    public ReturnYouTubeDislikeService()
        : this(CreateDefaultHttpClient(), true)
    {
    }

    public ReturnYouTubeDislikeService(IPreferencesService preferencesService)
        : this(CreateDefaultHttpClient(), preferencesService, true)
    {
    }

    public ReturnYouTubeDislikeService(HttpClient httpClient, bool disposeHttpClient = false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _disposeHttpClient = disposeHttpClient;
    }

    public ReturnYouTubeDislikeService(HttpClient httpClient, IPreferencesService preferencesService,
        bool disposeHttpClient = false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(preferencesService);
        _httpClient = httpClient;
        _preferencesService = preferencesService;
        _disposeHttpClient = disposeHttpClient;
    }

    public void Dispose()
    {
        if (_disposeHttpClient) _httpClient.Dispose();
    }

    public async Task<VideoEngagement?> GetEngagementAsync(string videoId,
        CancellationToken cancellationToken = default)
    {
        if (!PlaybackRequest.LooksLikeYouTubeVideoId(videoId)) return null;
        if (!IsEnabled())
        {
            // Fail closed: no network and no cached (possibly stale) entries while disabled.
            _engagementByVideoId.Clear();
            return null;
        }

        if (_engagementByVideoId.TryGetValue(videoId, out var cached))
        {
            Logger.Debug("ReturnYouTubeDislike cache hit for video {VideoId}", videoId);
            return cached;
        }

        var requestUri = new UriBuilder(VotesEndpoint)
        {
            Query = $"videoId={Uri.EscapeDataString(videoId)}"
        }.Uri;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
            {
                Logger.Warning("ReturnYouTubeDislike returned HTTP status {StatusCode} for video {VideoId}",
                    response.StatusCode, videoId);
                return null;
            }

            await using var responseStream =
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync(responseStream,
                    ReturnYouTubeDislikeJsonContext.Default.ReturnYouTubeDislikeResponse, cancellationToken)
                .ConfigureAwait(false);
            if (payload is null || !string.Equals(payload.Id, videoId, StringComparison.Ordinal)
                                || payload.Likes < 0 || payload.Dislikes < 0)
                return null;

            var engagement = new VideoEngagement(payload.Likes, payload.Dislikes);
            AddBounded(videoId, engagement);
            Logger.Information("Fetched engagement stats for video {VideoId}: {Likes} likes, {Dislikes} dislikes",
                videoId, payload.Likes, payload.Dislikes);
            return engagement;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or NotSupportedException)
        {
            Logger.Warning(exception, "Failed to fetch dislike counts for video {VideoId}", videoId);
            return null;
        }
    }

    private bool IsEnabled()
    {
        // No preferences handle (legacy/test construction): preserve the old always-on behavior.
        if (_preferencesService is null) return true;
        try
        {
            return _preferencesService.GetPreferences().RydEnabled;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Assuming RYD is disabled: preferences are unreadable");
            return false;
        }
    }

    private void AddBounded(string videoId, VideoEngagement engagement)
    {
        if (_engagementByVideoId.TryAdd(videoId, engagement))
            _insertionOrder.Enqueue(videoId);
        while (_engagementByVideoId.Count > MaxCachedVideos && _insertionOrder.TryDequeue(out var oldest))
            _engagementByVideoId.TryRemove(oldest, out _);
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        return new HttpClient { Timeout = DefaultTimeout };
    }
}

internal sealed record ReturnYouTubeDislikeResponse(string Id, long Likes, long Dislikes);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ReturnYouTubeDislikeResponse))]
internal partial class ReturnYouTubeDislikeJsonContext : JsonSerializerContext;