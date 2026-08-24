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
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.YouTube;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Infrastructure.Player.Comments;
using SilverScreen.Infrastructure.Browsing.Common;
using SilverScreen.Infrastructure.Browsing.Home;
using SilverScreen.Infrastructure.Browsing.Channel;
using SilverScreen.Infrastructure.Browsing.Search;
using SilverScreen.Infrastructure.Browsing.History;
using SilverScreen.Infrastructure.Queue;
using SilverScreen.Infrastructure.Account.Session;
using SilverScreen.Infrastructure.Account.Auth;
using SilverScreen.Infrastructure.Account.Profile;
using SilverScreen.Infrastructure.Preferences;
using SilverScreen.Shell;
using SilverScreen.Browsing.Components;
using SilverScreen.Browsing.Home;
using SilverScreen.Browsing.Channel;
using SilverScreen.Browsing.Search;
using SilverScreen.Browsing.History;
using SilverScreen.Player;
using SilverScreen.Player.Views;
using SilverScreen.Player.Controllers;
using SilverScreen.Player.Comments;
using SilverScreen.Queue;
using SilverScreen.Account.Profile;
using SilverScreen.Account.Auth;
using SilverScreen.Account.Session;
using SilverScreen.Preferences;


namespace SilverScreen.Tests.Browsing.Home;

public sealed class AuthenticatedHomeFeedServiceTests
{
    private const string CookieContent =
        "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t2147483647\tSID\tsession\n";


    [Fact]
    public async Task LoadFirstPageAsync_MapsAndCachesSuccessfulResults()
    {
        var client = new FakeYouTubeHomeClient
        {
            ResponseFactory = (_, _) => Task.FromResult(new HomeFeedResult(
                [CreateVideo("v1"), CreateVideo("v2")], "next", true, "OK", false))
        };
        var service = CreateService(client);

        var result = await service.LoadFirstPageAsync();

        Assert.Equal(AuthenticatedHomeFeedStatus.Success, result.Status);
        Assert.Equal(["v1", "v2"], result.FeedPage.Videos.Select(video => video.Id));
        Assert.Equal("next", service.GetHomeFeed().ContinuationToken);
    }

    [Fact]
    public async Task LoadFirstPageAsync_AuthenticationRejectionClearsCachedResults()
    {
        var client = new FakeYouTubeHomeClient
        {
            ResponseFactory = (_, _) => Task.FromResult(new HomeFeedResult(
                [CreateVideo("v1")], "next", true, "OK", false))
        };
        var service = CreateService(client);
        await service.LoadFirstPageAsync();
        client.ResponseFactory = (_, _) => Task.FromResult(new HomeFeedResult([], null, false, "Rejected", true));

        var result = await service.LoadFirstPageAsync();

        Assert.Equal(AuthenticatedHomeFeedStatus.AuthenticationRejected, result.Status);
        Assert.Empty(service.GetHomeFeed().Videos);
    }

    [Fact]
    public async Task LoadFirstPageAsync_BackendFailurePreservesCachedResults()
    {
        var client = new FakeYouTubeHomeClient
        {
            ResponseFactory = (_, _) => Task.FromResult(new HomeFeedResult(
                [CreateVideo("v1")], "next", true, "OK", false))
        };
        var service = CreateService(client);
        await service.LoadFirstPageAsync();
        client.ResponseFactory = (_, _) => Task.FromResult(new HomeFeedResult([], null, false, "Failure", false));

        var result = await service.LoadFirstPageAsync();

        Assert.Equal(AuthenticatedHomeFeedStatus.TemporaryBackendFailure, result.Status);
        Assert.Single(service.GetHomeFeed().Videos);
    }

    [Fact]
    public async Task LoadNextPageAsync_UsesContinuationAndAppendsToTheCache()
    {
        var client = new FakeYouTubeHomeClient
        {
            ResponseFactory = (_, _) => Task.FromResult(new HomeFeedResult(
                [CreateVideo("v1")], "next", true, "OK", false))
        };
        var service = CreateService(client);
        await service.LoadFirstPageAsync();
        client.ResponseFactory = (_, _) => Task.FromResult(new HomeFeedResult(
            [CreateVideo("v2")], null, true, "OK", false));

        var result = await service.LoadNextPageAsync();

        Assert.Equal("next", client.LastContinuationToken);
        Assert.Equal(["v2"], result.FeedPage.Videos.Select(video => video.Id));
        Assert.Equal(["v1", "v2"], service.GetHomeFeed().Videos.Select(video => video.Id));
    }

    private static AuthenticatedHomeFeedService CreateService(FakeYouTubeHomeClient client)
    {
        var session = new InMemorySessionService();
        session.SetManualSession(CookieContent, SessionCookieFormat.NetscapeCookiesText);
        return new AuthenticatedHomeFeedService(client, session);
    }

    private static VideoSummary CreateVideo(string id)
    {
        return new VideoSummary(id, $"Video {id}", "Channel", TimeSpan.FromMinutes(3), "thumbnail", false);
    }

    private sealed class FakeYouTubeHomeClient : IYouTubeHomeClient
    {
        public string? LastContinuationToken { get; private set; }
        public Func<string?, CancellationToken, Task<HomeFeedResult>>? ResponseFactory { get; set; }

        public Task<HomeFeedResult> GetHomeFeedAsync(string? continuationToken = null,
            CancellationToken cancellationToken = default)
        {
            LastContinuationToken = continuationToken;
            return ResponseFactory?.Invoke(continuationToken, cancellationToken)
                   ?? Task.FromResult(new HomeFeedResult([], null, true, "OK", false));
        }
    }
}
