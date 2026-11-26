using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Common;

namespace SilverScreen.Infrastructure.Player;

/// <summary>
///     Fetches dislike estimates from returnyoutubedislike.com. Off by default: unless
///     <see cref="AppPreferences.RydEnabled" /> is true, every call fails closed to null —
///     no HTTP traffic and no cached (possibly stale) entries are served. The per-video
///     cache is bounded to <see cref="MaxCachedVideos" /> LRU entries with singleflight deduplication.
/// </summary>
public sealed class ReturnYouTubeDislikeService : IVideoEngagementService, IDisposable
{
    /// <summary>Maximum cached videos; the least-recently-used entry is evicted past this bound.</summary>
    internal const int MaxCachedVideos = 100;

    private static readonly ILogger Logger = Log.ForContext<ReturnYouTubeDislikeService>();
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);
    private static readonly Uri VotesEndpoint = new("https://returnyoutubedislikeapi.com/votes");
    private readonly BoundedLruCache<string, VideoEngagement> _cache = new(MaxCachedVideos, StringComparer.Ordinal);
    private readonly bool _disposeHttpClient;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, Lazy<Task<VideoEngagement?>>> _inFlight = new(StringComparer.Ordinal);
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
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        if (_disposeHttpClient) _httpClient.Dispose();
    }

    public async Task<VideoEngagement?> GetEngagementAsync(string videoId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!PlaybackRequest.LooksLikeYouTubeVideoId(videoId)) return null;
        if (!IsEnabled())
        {
            // Fail closed: no network and no cached (possibly stale) entries while disabled.
            _cache.Clear();
            return null;
        }

        if (_cache.TryGetValue(videoId, out var cached))
        {
            Logger.Debug("ReturnYouTubeDislike cache hit for video");
            return cached;
        }

        Lazy<Task<VideoEngagement?>>? created = null;
        var lazy = _inFlight.GetOrAdd(videoId, _ =>
        {
            created = new Lazy<Task<VideoEngagement?>>(
                () => ExecuteSingleflightAsync(videoId, created!),
                LazyThreadSafetyMode.ExecutionAndPublication);
            return created;
        });

        var result = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!IsEnabled())
        {
            _cache.Clear();
            return null;
        }

        return result;
    }

    private async Task<VideoEngagement?> ExecuteSingleflightAsync(
        string videoId,
        Lazy<Task<VideoEngagement?>> self)
    {
        try
        {
            return await FetchAndCacheAsync(videoId, _disposeCts.Token).ConfigureAwait(false);
        }
        finally
        {
            _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<VideoEngagement?>>>(videoId, self));
        }
    }

    private async Task<VideoEngagement?> FetchAndCacheAsync(string videoId, CancellationToken cancellationToken)
    {
        if (!IsEnabled())
        {
            _cache.Clear();
            return null;
        }

        if (_cache.TryGetValue(videoId, out var cached))
        {
            Logger.Debug("ReturnYouTubeDislike cache hit for video");
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
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
            {
                Logger.Warning("ReturnYouTubeDislike returned HTTP status {StatusCode}", response.StatusCode);
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
            if (IsEnabled())
                _cache.Set(videoId, engagement);
            return engagement;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or NotSupportedException)
        {
            Logger.Warning(exception, "Failed to fetch dislike counts for video");
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

    private static HttpClient CreateDefaultHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 8,
            EnableMultipleHttp2Connections = true,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(90),
            AutomaticDecompression = DecompressionMethods.All
        };
        return new HttpClient(handler, true) { Timeout = DefaultTimeout };
    }
}

internal sealed record ReturnYouTubeDislikeResponse(string Id, long Likes, long Dislikes);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ReturnYouTubeDislikeResponse))]
internal partial class ReturnYouTubeDislikeJsonContext : JsonSerializerContext;
