using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Core.Browsing.Home;

public interface IAuthenticatedHomeFeedService
{
    Task<AuthenticatedHomeFeedResult> LoadFirstPageAsync(int count = VideoFeedConstants.DefaultPageSize,
        CancellationToken cancellationToken = default);

    Task<AuthenticatedHomeFeedResult> LoadNextPageAsync(int count = VideoFeedConstants.DefaultPageSize,
        CancellationToken cancellationToken = default);
}