using SilverScreen.Core.Models;

namespace SilverScreen.Core.Services;

public interface IVideoEngagementService
{
    Task<VideoEngagement?> GetEngagementAsync(string videoId, CancellationToken cancellationToken = default);
}
