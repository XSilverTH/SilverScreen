using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using SilverScreen.Core.Player;
using SilverScreen.Infrastructure.Common;

namespace SilverScreen.Infrastructure.Player;

/// <summary>
///     Fetches skip segments from sponsor.ajay.app. Results are cached per video+categories request,
///     bounded to <see cref="MaxCachedRequests" /> LRU entries, with TTL-bounded negative caching for
///     404/empty responses and per-request singleflight deduplication.
/// </summary>
public sealed class SponsorBlockService : ISponsorBlockService, IDisposable
{
    /// <summary>Maximum cached video+categories requests; least-recently-used entry evicted past this bound.</summary>
    internal const int MaxCachedRequests = 100;

    internal static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromMinutes(10);

    private static readonly ILogger Logger = Log.ForContext<SponsorBlockService>();
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly Uri SkipSegmentsEndpoint = new("https://sponsor.ajay.app/api/skipSegments");
    private readonly BoundedLruCache<string, IReadOnlyList<SponsorBlockSegment>> _cache;
    private readonly bool _disposeHttpClient;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<SponsorBlockSegment>>>> _inFlight = new(StringComparer.Ordinal);

    public SponsorBlockService() : this(CreateDefaultHttpClient(), true)
    {
    }

    public SponsorBlockService(HttpClient httpClient, bool disposeHttpClient = false, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _disposeHttpClient = disposeHttpClient;
        _cache = new BoundedLruCache<string, IReadOnlyList<SponsorBlockSegment>>(
            MaxCachedRequests,
            StringComparer.Ordinal,
            timeProvider);
    }

    public void Dispose()
    {
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        if (_disposeHttpClient) _httpClient.Dispose();
    }

    public async Task<IReadOnlyList<SponsorBlockSegment>> GetSegmentsAsync(string videoId,
        IReadOnlyCollection<string> categories, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(categories);
        cancellationToken.ThrowIfCancellationRequested();
        if (!PlaybackRequest.LooksLikeYouTubeVideoId(videoId)) return [];

        var selectedCategories = categories
            .Where(SponsorBlockCategories.All.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (selectedCategories.Length == 0) return [];

        var cacheKey = BuildCacheKey(videoId, selectedCategories);
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            Logger.Debug("SponsorBlock cache hit for video");
            return cached;
        }

        Lazy<Task<IReadOnlyList<SponsorBlockSegment>>>? created = null;
        var lazy = _inFlight.GetOrAdd(cacheKey, _ =>
        {
            created = new Lazy<Task<IReadOnlyList<SponsorBlockSegment>>>(
                () => ExecuteSingleflightAsync(videoId, selectedCategories, cacheKey, created!),
                LazyThreadSafetyMode.ExecutionAndPublication);
            return created;
        });

        return await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<SponsorBlockSegment>> ExecuteSingleflightAsync(
        string videoId,
        string[] selectedCategories,
        string cacheKey,
        Lazy<Task<IReadOnlyList<SponsorBlockSegment>>> self)
    {
        try
        {
            return await FetchAndCacheAsync(videoId, selectedCategories, cacheKey, _disposeCts.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            _inFlight.TryRemove(
                new KeyValuePair<string, Lazy<Task<IReadOnlyList<SponsorBlockSegment>>>>(cacheKey, self));
        }
    }

    private async Task<IReadOnlyList<SponsorBlockSegment>> FetchAndCacheAsync(
        string videoId,
        string[] selectedCategories,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            Logger.Debug("SponsorBlock cache hit for video");
            return cached;
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(videoId));
        var hashPrefix = Convert.ToHexStringLower(hashBytes)[..4];

        var query = "actionType=skip&" +
                    string.Join('&',
                        selectedCategories.Select(category => $"category={Uri.EscapeDataString(category)}"));
        var requestUri = new UriBuilder($"{SkipSegmentsEndpoint}/{hashPrefix}") { Query = query }.Uri;

        try
        {
            Logger.Information("Fetching SponsorBlock segments with categories {Categories}", selectedCategories);
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                IReadOnlyList<SponsorBlockSegment> empty = [];
                _cache.Set(cacheKey, empty, NegativeCacheTtl);
                return empty;
            }

            if (response.StatusCode is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
            {
                Logger.Warning("SponsorBlock request returned HTTP status {StatusCode}", response.StatusCode);
                return [];
            }

            await using var responseStream =
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync(responseStream,
                    SponsorBlockJsonContext.Default.SponsorBlockVideoResponseArray, cancellationToken)
                .ConfigureAwait(false);
            if (payload is null) return [];

            var videoEntry = payload.FirstOrDefault(v => string.Equals(v.VideoId, videoId, StringComparison.Ordinal));
            if (videoEntry?.Segments is null)
            {
                IReadOnlyList<SponsorBlockSegment> empty = [];
                _cache.Set(cacheKey, empty, NegativeCacheTtl);
                return empty;
            }

            var segments = videoEntry.Segments
                .Where(segment => segment is
                                  {
                                      Id.Length: > 0,
                                      Category: not null,
                                      ActionType: "skip",
                                      Segment: [var start, var end]
                                  } && selectedCategories.Contains(segment.Category, StringComparer.Ordinal) &&
                                  IsValidTimeRange(start, end))
                .Select(segment => new SponsorBlockSegment(segment.Id!, TimeSpan.FromSeconds(segment.Segment![0]),
                    TimeSpan.FromSeconds(segment.Segment[1]), segment.Category!))
                .OrderBy(segment => segment.Start)
                .ToArray();

            _cache.Set(cacheKey, segments);
            Logger.Debug("Fetched {Count} SponsorBlock segments", segments.Length);
            return segments;
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or NotSupportedException)
        {
            Logger.Warning(exception, "Failed to fetch SponsorBlock segments for video");
            return [];
        }
    }

    private static string BuildCacheKey(string videoId, string[] selectedCategories)
    {
        if (selectedCategories.Length == 1)
            return $"{videoId}\n{selectedCategories[0]}";

        var sorted = (string[])selectedCategories.Clone();
        Array.Sort(sorted, StringComparer.Ordinal);
        return $"{videoId}\n{string.Join(',', sorted)}";
    }

    private static bool IsValidTimeRange(double start, double end)
    {
        return start >= 0 && end > start && !double.IsNaN(start) && !double.IsInfinity(start) &&
               !double.IsNaN(end) && !double.IsInfinity(end) && end <= TimeSpan.MaxValue.TotalSeconds;
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

internal sealed record SponsorBlockVideoResponse(
    [property: JsonPropertyName("videoID")]
    string? VideoId,
    [property: JsonPropertyName("segments")]
    SponsorBlockSkipSegment[]? Segments);

internal sealed record SponsorBlockSkipSegment(double[]? Segment, string? Uuid, string? Category, string? ActionType)
{
    public string? Id => Uuid;
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SponsorBlockVideoResponse[]))]
[JsonSerializable(typeof(SponsorBlockSkipSegment[]))]
internal partial class SponsorBlockJsonContext : JsonSerializerContext;
