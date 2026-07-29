using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Features.Feed;
using SilverScreen.Features.Session;
using SilverScreen.Infrastructure.Features.Session;
using SilverScreen.ViewModels;

namespace SilverScreen.Tests;

public sealed class AccountViewModelTests
{
    [Fact]
    public void Constructor_ShowsCachedProfileWhileBackgroundRefreshFails()
    {
        var session = new InMemorySessionService();
        session.SetManualSession("youtube.com\tTRUE\t/\tTRUE\t2147483647\tSAPISID\tfake-sapisid",
            SessionCookieFormat.NetscapeCookiesText);
        var expected = new AccountProfile("Silver", "https://example.com/avatar.png");
        var profileService = new CachedProfileService(expected);
        using var validation =
            new SessionValidationCoordinator(new HomeSessionValidator(new NoOpFeedService()), session);
        using var viewModel = new AccountViewModel(profileService, session, validation, new CapturingStatusReporter());

        Assert.Equal(expected.DisplayName, viewModel.DisplayName);
        Assert.Equal(expected.AvatarUrl, viewModel.AvatarUrl);
    }

    private sealed class CachedProfileService(AccountProfile cachedProfile) : IAccountProfileService
    {
        public AccountProfile? GetCachedProfile()
        {
            return cachedProfile;
        }

        public Task<AccountProfile?> GetCurrentProfileAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<AccountProfile?>(null);
        }
    }

    private sealed class NoOpFeedService : IAuthenticatedHomeFeedService
    {
        public FeedPage GetHomeFeed()
        {
            return FeedPage.Empty;
        }

        public Task<AuthenticatedHomeFeedResult> LoadFirstPageAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<AuthenticatedHomeFeedResult> LoadNextPageAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class CapturingStatusReporter : IStatusReporter
    {
        public void ReportStatus(string message)
        {
        }
    }
}