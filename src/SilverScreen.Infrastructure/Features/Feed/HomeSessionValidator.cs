using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Features.Feed;

public sealed class HomeSessionValidator(IAuthenticatedHomeFeedService feedService)
{
    private readonly IAuthenticatedHomeFeedService _feedService =
        feedService ?? throw new ArgumentNullException(nameof(feedService));

    public async Task<HomeSessionValidationResult> ValidateSessionAsync(CancellationToken cancellationToken = default)
    {
        var result = await _feedService.LoadFirstPageAsync(cancellationToken);

        var isSuccess = result.Status == AuthenticatedHomeFeedStatus.Success;
        var videoCount = result.FeedPage?.Videos?.Count ?? 0;
        var hasContinuation = !string.IsNullOrEmpty(result.FeedPage?.ContinuationToken);
        var requiresAuth = result.Status == AuthenticatedHomeFeedStatus.AuthenticationRequired ||
                           result.Status == AuthenticatedHomeFeedStatus.AuthenticationRejected;

        return new HomeSessionValidationResult(
            IsSuccess: isSuccess,
            VideoCount: videoCount,
            HasContinuation: hasContinuation,
            RequiresAuthentication: requiresAuth,
            HighLevelStatus: result.Status,
            StatusMessage: result.StatusMessage
        );
    }
}