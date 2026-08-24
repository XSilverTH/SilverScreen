namespace SilverScreen.Core.Player;

public interface IVideoEngagementService
{
    Task<VideoEngagement?> GetEngagementAsync(string videoId, CancellationToken cancellationToken = default);
}