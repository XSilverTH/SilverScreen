using System;
using System.Threading;
using System.Threading.Tasks;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Features.Feed;

public sealed class HomeSessionValidator
{
    private readonly IAuthenticatedHomeFeedService _feedService;

    public HomeSessionValidator(IAuthenticatedHomeFeedService feedService)
    {
        _feedService = feedService ?? throw new ArgumentNullException(nameof(feedService));
    }

    public async Task<HomeSessionValidationResult> ValidateSessionAsync(CancellationToken cancellationToken = default)
    {
        var result = await _feedService.LoadFirstPageAsync(cancellationToken);

        bool isSuccess = result.Status == AuthenticatedHomeFeedStatus.Success;
        int videoCount = result.FeedPage?.Videos?.Count ?? 0;
        bool hasContinuation = !string.IsNullOrEmpty(result.FeedPage?.ContinuationToken);
        bool requiresAuth = result.Status == AuthenticatedHomeFeedStatus.AuthenticationRequired ||
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
