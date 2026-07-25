using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Features.Engagement;

public sealed class ReturnYouTubeDislikeService : IVideoEngagementService, IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly Uri VotesEndpoint = new("https://returnyoutubedislikeapi.com/votes");
    private readonly ConcurrentDictionary<string, VideoEngagement> _engagementByVideoId = new(StringComparer.Ordinal);
    private readonly bool _disposeHttpClient;
    private readonly HttpClient _httpClient;

    public ReturnYouTubeDislikeService()
        : this(CreateDefaultHttpClient(), disposeHttpClient: true)
    {
    }

    public ReturnYouTubeDislikeService(HttpClient httpClient, bool disposeHttpClient = false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
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
        if (_engagementByVideoId.TryGetValue(videoId, out var cached)) return cached;

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
                return null;

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync(responseStream,
                    ReturnYouTubeDislikeJsonContext.Default.ReturnYouTubeDislikeResponse, cancellationToken)
                .ConfigureAwait(false);
            if (payload is null || !string.Equals(payload.Id, videoId, StringComparison.Ordinal)
                                || payload.Likes < 0 || payload.Dislikes < 0)
                return null;

            var engagement = new VideoEngagement(payload.Likes, payload.Dislikes);
            _engagementByVideoId.TryAdd(videoId, engagement);
            return engagement;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        return new HttpClient { Timeout = DefaultTimeout };
    }
}

internal sealed record ReturnYouTubeDislikeResponse(string Id, long Likes, long Dislikes);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ReturnYouTubeDislikeResponse))]
internal partial class ReturnYouTubeDislikeJsonContext : JsonSerializerContext
{
}
