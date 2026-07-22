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
        var requiresAuth = result.Status is AuthenticatedHomeFeedStatus.AuthenticationRequired
            or AuthenticatedHomeFeedStatus.AuthenticationRejected;

        return new HomeSessionValidationResult(
            isSuccess,
            videoCount,
            hasContinuation,
            requiresAuth,
            result.Status,
            result.StatusMessage
        );
    }
}