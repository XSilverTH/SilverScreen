using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using SilverScreen.Core.Player;

namespace SilverScreen.Infrastructure.Player;

/// <summary>
///     Fetches skip segments from sponsor.ajay.app. Results are cached per video+categories request,
///     bounded to <see cref="MaxCachedRequests" /> entries (oldest-inserted evicted). Callers gate
///     network access behind the SponsorBlock toggles (no fetch unless auto-skip or display is on).
/// </summary>
public sealed class SponsorBlockService : ISponsorBlockService, IDisposable
{
    /// <summary>Maximum cached video+categories requests; oldest-inserted entry evicted past this bound.</summary>
    internal const int MaxCachedRequests = 100;

    private static readonly ILogger Logger = Log.ForContext<SponsorBlockService>();
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly Uri SkipSegmentsEndpoint = new("https://sponsor.ajay.app/api/skipSegments");
    private readonly bool _disposeHttpClient;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentQueue<string> _insertionOrder = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<SponsorBlockSegment>> _segmentsByRequest = new();

    public SponsorBlockService() : this(CreateDefaultHttpClient(), true)
    {
    }

    public SponsorBlockService(HttpClient httpClient, bool disposeHttpClient = false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _disposeHttpClient = disposeHttpClient;
    }

    public void Dispose()
    {
        if (_disposeHttpClient) _httpClient.Dispose();
    }

    public async Task<IReadOnlyList<SponsorBlockSegment>> GetSegmentsAsync(string videoId,
        IReadOnlyCollection<string> categories, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(categories);
        if (!PlaybackRequest.LooksLikeYouTubeVideoId(videoId)) return [];

        var selectedCategories = categories
            .Where(SponsorBlockCategories.All.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (selectedCategories.Length == 0) return [];

        var cacheKey = $"{videoId}\n{string.Join(',', selectedCategories)}";
        if (_segmentsByRequest.TryGetValue(cacheKey, out var cached))
        {
            Logger.Debug("SponsorBlock cache hit for video {VideoId}", videoId);
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
            Logger.Information("Fetching SponsorBlock segments for video {VideoId} with categories {Categories}",
                videoId, selectedCategories);
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
            {
                if (response.StatusCode != HttpStatusCode.NotFound)
                    Logger.Warning(
                        "SponsorBlock request for video {VideoId} (prefix {HashPrefix}) returned HTTP status {StatusCode}",
                        videoId, hashPrefix, response.StatusCode);
                return [];
            }

            await using var responseStream =
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync(responseStream,
                    SponsorBlockJsonContext.Default.SponsorBlockVideoResponseArray, cancellationToken)
                .ConfigureAwait(false);
            if (payload is null) return [];

            var videoEntry = payload.FirstOrDefault(v => string.Equals(v.VideoId, videoId, StringComparison.Ordinal));
            if (videoEntry?.Segments is null) return [];
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

            AddBounded(cacheKey, segments);
            Logger.Information("Fetched {Count} SponsorBlock segments for video {VideoId}", segments.Length, videoId);
            return segments;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or NotSupportedException)
        {
            Logger.Warning(exception, "Failed to fetch SponsorBlock segments for video {VideoId}", videoId);
            return [];
        }
    }

    private void AddBounded(string cacheKey, IReadOnlyList<SponsorBlockSegment> segments)
    {
        if (_segmentsByRequest.TryAdd(cacheKey, segments))
            _insertionOrder.Enqueue(cacheKey);
        while (_segmentsByRequest.Count > MaxCachedRequests && _insertionOrder.TryDequeue(out var oldest))
            _segmentsByRequest.TryRemove(oldest, out _);
    }

    private static bool IsValidTimeRange(double start, double end)
    {
        return start >= 0 && end > start && !double.IsNaN(start) && !double.IsInfinity(start) &&
               !double.IsNaN(end) && !double.IsInfinity(end) && end <= TimeSpan.MaxValue.TotalSeconds;
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        return new HttpClient { Timeout = DefaultTimeout };
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