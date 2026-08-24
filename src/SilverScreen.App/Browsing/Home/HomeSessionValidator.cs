using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Search;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Queue;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Browsing.Home;

public sealed class HomeSessionValidator(IAuthenticatedHomeFeedService feedService)
{
    private readonly IAuthenticatedHomeFeedService _feedService =
        feedService ?? throw new ArgumentNullException(nameof(feedService));

    public async Task<HomeSessionValidationResult> ValidateSessionAsync(CancellationToken cancellationToken = default)
    {
        var result = await _feedService.LoadFirstPageAsync(cancellationToken);

        var isSuccess = result.Status == AuthenticatedHomeFeedStatus.Success;
        var videoCount = result.FeedPage.Videos.Count;
        var hasContinuation = !string.IsNullOrEmpty(result.FeedPage.ContinuationToken);
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