using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Features.Playback;

public sealed class SponsorBlockService : ISponsorBlockService, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<SponsorBlockService>();
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly Uri SkipSegmentsEndpoint = new("https://sponsor.ajay.app/api/skipSegments");
    private readonly bool _disposeHttpClient;
    private readonly HttpClient _httpClient;
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

        var query = $"videoID={Uri.EscapeDataString(videoId)}&actionType=skip&" +
                    string.Join('&',
                        selectedCategories.Select(category => $"category={Uri.EscapeDataString(category)}"));
        var requestUri = new UriBuilder(SkipSegmentsEndpoint) { Query = query }.Uri;

        try
        {
            Logger.Information("Fetching SponsorBlock segments for video {VideoId} with categories {Categories}",
                videoId, selectedCategories);
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
            {
                Logger.Warning("SponsorBlock request for video {VideoId} returned HTTP status {StatusCode}", videoId,
                    response.StatusCode);
                return [];
            }

            await using var responseStream =
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync(responseStream,
                    SponsorBlockJsonContext.Default.SponsorBlockSkipSegmentArray, cancellationToken)
                .ConfigureAwait(false);
            if (payload is null) return [];

            var segments = payload
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

            _segmentsByRequest.TryAdd(cacheKey, segments);
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

internal sealed record SponsorBlockSkipSegment(double[]? Segment, string? Uuid, string? Category, string? ActionType)
{
    public string? Id => Uuid;
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SponsorBlockSkipSegment[]))]
internal partial class SponsorBlockJsonContext : JsonSerializerContext;