using Serilog;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Player;
using SilverScreen.Infrastructure.YouTube;
using YoutubeAPI.Exceptions;
using YoutubeAPI.Models.Enums;
using YoutubeAPI.Models.ValueTypes;

namespace SilverScreen.Infrastructure.Player;

/// <summary>Reads and changes YouTube video ratings through the typed YoutubeAPI client.</summary>
public sealed class YoutubeApiRatingService(
    IYouTubeClientProvider clientProvider,
    ISessionService sessionService) : IYouTubeRatingService
{
    private static readonly ILogger Logger = Log.ForContext<YoutubeApiRatingService>();
    private readonly IYouTubeClientProvider _clientProvider =
        clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));
    private readonly ISessionService _sessionService =
        sessionService ?? throw new ArgumentNullException(nameof(sessionService));

    public async Task<YouTubeRatingState> GetRatingStateAsync(
        string videoId,
        CancellationToken cancellationToken = default)
    {
        if (!VideoId.TryParse(videoId, out var parsedVideoId) || !HasAuthenticatedSession())
            return YouTubeRatingState.None;

        try
        {
            var rating = await _clientProvider.GetClient().Ratings
                .GetAsync(parsedVideoId, cancellationToken)
                .ConfigureAwait(false);
            if (!HasAuthenticatedSession())
                return YouTubeRatingState.None;

            return rating switch
            {
                VideoRating.Like => YouTubeRatingState.Like,
                VideoRating.Dislike => YouTubeRatingState.Dislike,
                _ => YouTubeRatingState.None
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (YouTubeException exception)
        {
            Logger.Debug(exception, "YoutubeAPI failed to read rating for video {VideoId}", videoId);
            return YouTubeRatingState.None;
        }
        catch (Exception exception)
        {
            Logger.Debug(exception, "Unexpected failure reading rating for video {VideoId}", videoId);
            return YouTubeRatingState.None;
        }
    }

    public Task<bool> SubmitVoteAsync(
        string videoId,
        VideoVote vote,
        CancellationToken cancellationToken = default)
    {
        var rating = vote switch
        {
            VideoVote.Like => VideoRating.Like,
            VideoVote.Dislike => VideoRating.Dislike,
            _ => (VideoRating?)null
        };
        return rating is null
            ? Task.FromResult(false)
            : SetRatingAsync(videoId, rating.Value, cancellationToken);
    }

    public Task<bool> RemoveVoteAsync(
        string videoId,
        VideoVote vote,
        CancellationToken cancellationToken = default)
    {
        return vote is not (VideoVote.Like or VideoVote.Dislike)
            ? Task.FromResult(false)
            : SetRatingAsync(videoId, VideoRating.None, cancellationToken);
    }

    private async Task<bool> SetRatingAsync(
        string videoId,
        VideoRating rating,
        CancellationToken cancellationToken)
    {
        if (!VideoId.TryParse(videoId, out var parsedVideoId) || !HasAuthenticatedSession())
            return false;

        try
        {
            await _clientProvider.GetClient().Ratings
                .SetAsync(parsedVideoId, rating, cancellationToken)
                .ConfigureAwait(false);
            return HasAuthenticatedSession();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (YouTubeException exception)
        {
            Logger.Warning(exception, "YoutubeAPI failed to set rating {Rating} for video {VideoId}", rating, videoId);
            return false;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Unexpected failure setting rating {Rating} for video {VideoId}", rating,
                videoId);
            return false;
        }
    }

    private bool HasAuthenticatedSession()
    {
        var session = _sessionService.GetCurrentSession();
        var cookies = _sessionService.GetManualSessionCookies();
        return session is { IsSignedIn: true, HasManualSession: true } &&
               cookies is { Format: SessionCookieFormat.NetscapeCookiesText } &&
               !string.IsNullOrWhiteSpace(cookies.Content);
    }
}
