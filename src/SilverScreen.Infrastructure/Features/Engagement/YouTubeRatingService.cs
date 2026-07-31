using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Infrastructure.Features.Engagement;

public sealed class YouTubeRatingService : IYouTubeRatingService, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<YouTubeRatingService>();
    private static readonly Regex LikeStatusRegex = new(
        """\\?"likeStatus\\?"\s*:\s*\\?"(LIKE|DISLIKE|INDIFFERENT)\\?""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex LikeParamsRegex = CreateParameterRegex("likeParams");
    private static readonly Regex DislikeParamsRegex = CreateParameterRegex("dislikeParams");
    private static readonly Regex RemoveLikeParamsRegex = CreateParameterRegex("removeLikeParams");
    private readonly YouTubeAuthenticationService _authentication;
    private readonly HttpClient _httpClient;

    private readonly ConcurrentDictionary<string, RatingMetadata>
        _ratingMetadataByVideoId = new(StringComparer.Ordinal);

    public YouTubeRatingService(HttpClient httpClient, YouTubeAuthenticationService authentication)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _authentication.CredentialsChanged += OnCredentialsChanged;
    }

    public void Dispose()
    {
        _authentication.CredentialsChanged -= OnCredentialsChanged;
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

        Logger.Information("Submitting rating action '{Action}' (Vote: {Vote}) for video {VideoId}", action, vote, videoId);
        var metadata = _ratingMetadataByVideoId.TryGetValue(videoId, out var cached)
            ? cached
            : await LoadRatingMetadataAsync(videoId, cancellationToken).ConfigureAwait(false);
        if (metadata is null) return false;

        var currentCredentials = _authentication.GetCurrentCredentials();
        if (currentCredentials is null || currentCredentials.SessionVersion != metadata.SessionVersion)
            return false;

        try
        {
            var authenticatedRequest = await CreateAuthenticatedRequestAsync($"like/{action}", videoId,
                metadata,
                context => new RatingRequestPayload
                {
                    Context = context,
                    Target = new RatingTarget { VideoId = videoId },
                    Params = ActionParams(action, metadata)
                },
                YouTubeRequestJsonContext.Default.RatingRequestPayload, cancellationToken).ConfigureAwait(false);
            if (authenticatedRequest is null ||
                authenticatedRequest.Session.CredentialSnapshot.SessionVersion != metadata.SessionVersion)
                return false;

            if (!_authentication.IsCurrent(authenticatedRequest.Session.CredentialSnapshot.SessionVersion))
                return false;
            using var request = authenticatedRequest.Request;
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                _ratingMetadataByVideoId.TryRemove(videoId, out _);
                Logger.Information("Successfully submitted rating action '{Action}' for video {VideoId}", action, videoId);
            }
            else
            {
                Logger.Warning("Failed rating action '{Action}' for video {VideoId}: HTTP status {StatusCode}", action, videoId, response.StatusCode);
            }
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
                                              or InvalidOperationException)
        {
            Logger.Warning(exception, "Exception submitting rating action '{Action}' for video {VideoId}", action, videoId);
            return false;
        }
    }

    private async Task<RatingMetadata?> LoadRatingMetadataAsync(string videoId, CancellationToken cancellationToken)
    {
        var credentials = _authentication.GetCurrentCredentials();
        if (credentials is null) return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, PlaybackRequest.BuildWatchUrl(videoId));
            _authentication.ApplyWatchPageHeaders(request, credentials, true);
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
                RemoveLikeParamsRegex.Match(html).Groups[1].Value,
                credentials.SessionVersion);
            if (!_authentication.IsCurrent(credentials.SessionVersion))
                return null;

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

    private async Task<AuthenticatedRequest?> CreateAuthenticatedRequestAsync<T>(string endpoint, string videoId,
        RatingMetadata metadata, Func<BrowseRequestContext, T> createPayload,
        JsonTypeInfo<T> payloadTypeInfo,
        CancellationToken cancellationToken) where T : class
    {
        var session = await _authentication
            .GetCurrentAsync(_httpClient, true, cancellationToken)
            .ConfigureAwait(false);
        if (session is null || session.CredentialSnapshot.SessionVersion != metadata.SessionVersion)
            return null;

        var payload = createPayload(CreateContext(videoId, session.Configuration));
        var requestUrl =
            $"https://www.youtube.com/youtubei/v1/{endpoint}?key={Uri.EscapeDataString(session.Configuration.ApiKey)}&prettyPrint=false";
        var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, payloadTypeInfo), Encoding.UTF8,
                "application/json")
        };
        _authentication.ApplyAuthenticatedHeaders(request, session, true);
        return new AuthenticatedRequest(request, session);
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
                Authuser = _authentication.AuthUser
            }
        };
    }

    private void OnCredentialsChanged(object? sender, EventArgs args)
    {
        _ratingMetadataByVideoId.Clear();
    }

    private static Regex CreateParameterRegex(string propertyName)
    {
        return new Regex($"\\\\?\"{Regex.Escape(propertyName)}\\\\?\"\\s*:\\s*\\\\?\"([^\"\\\\]+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private sealed record RatingMetadata(
        YouTubeRatingState State,
        string LikeParams,
        string DislikeParams,
        string RemoveLikeParams,
        long SessionVersion);

    private sealed record AuthenticatedRequest(
        HttpRequestMessage Request,
        YouTubeAuthenticationService.YouTubeAuthenticatedSession Session);
}