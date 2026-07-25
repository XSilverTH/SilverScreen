using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Feed;
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
        using var validation = new SessionValidationCoordinator(new HomeSessionValidator(new NoOpFeedService()), session);
        using var viewModel = new AccountViewModel(profileService, session, validation, new ShellViewModel());

        Assert.Equal(expected.DisplayName, viewModel.DisplayName);
        Assert.Equal(expected.AvatarUrl, viewModel.AvatarUrl);
    }

    private sealed class CachedProfileService(AccountProfile cachedProfile) : IAccountProfileService
    {
        public AccountProfile? GetCachedProfile() => cachedProfile;

        public Task<AccountProfile?> GetCurrentProfileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountProfile?>(null);
    }

    private sealed class NoOpFeedService : IAuthenticatedHomeFeedService
    {
        public FeedPage GetHomeFeed() => FeedPage.Empty;

        public Task<AuthenticatedHomeFeedResult> LoadFirstPageAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AuthenticatedHomeFeedResult> LoadNextPageAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
