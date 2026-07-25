using SilverScreen.Core.Models;

namespace SilverScreen.Core.Services;

public interface IYouTubeRatingService
{
    Task<YouTubeRatingState> GetRatingStateAsync(string videoId, CancellationToken cancellationToken = default);

    Task<bool> SubmitVoteAsync(string videoId, VideoVote vote, CancellationToken cancellationToken = default);

    Task<bool> RemoveVoteAsync(string videoId, VideoVote vote, CancellationToken cancellationToken = default);
}