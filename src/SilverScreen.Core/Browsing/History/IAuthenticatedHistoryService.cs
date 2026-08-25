using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Core.Browsing.History;

/// <summary>Loads the signed-in account's watch history from YouTube.</summary>
public interface IAuthenticatedHistoryService
{
    Task<AuthenticatedHistoryResult> LoadFirstPageAsync(int count = VideoFeedConstants.DefaultPageSize,
        CancellationToken cancellationToken = default);

    Task<AuthenticatedHistoryResult> LoadNextPageAsync(int count = VideoFeedConstants.DefaultPageSize,
        CancellationToken cancellationToken = default);
}