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


namespace SilverScreen.Tests.Browsing.History;

public sealed class AuthenticatedHistoryServiceTests
{
    private const string CookieContent =
        "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t2147483647\tSID\tsession\n";


    [Fact]
    public async Task LoadNextPageAsync_UsesContinuationAndOnlyReturnsNewPage()
    {
        var client = new FakeYouTubeHistoryClient
        {
            ResponseFactory = (_, _) => Task.FromResult(new HistoryFeedResult(
                [CreateVideo("v1")], "next", true, "OK", false))
        };
        using var service = CreateService(client);
        await service.LoadFirstPageAsync();
        client.ResponseFactory = (_, _) => Task.FromResult(new HistoryFeedResult(
            [CreateVideo("v1"), CreateVideo("v2")], null, true, "OK", false));

        var result = await service.LoadNextPageAsync();

        Assert.Equal("next", client.LastContinuationToken);
        Assert.Equal(["v1", "v2"], result.FeedPage.Videos.Select(video => video.Id));
    }

    private static AuthenticatedHistoryService CreateService(FakeYouTubeHistoryClient client)
    {
        var session = new InMemorySessionService();
        session.SetManualSession(CookieContent, SessionCookieFormat.NetscapeCookiesText);
        return new AuthenticatedHistoryService(client, session);
    }

    private static VideoSummary CreateVideo(string id)
    {
        return new VideoSummary(id, $"Video {id}", "Channel", TimeSpan.FromMinutes(3), "thumbnail", false);
    }

    private sealed class FakeYouTubeHistoryClient : IYouTubeHistoryClient
    {
        public string? LastContinuationToken { get; private set; }
        public Func<string?, CancellationToken, Task<HistoryFeedResult>>? ResponseFactory { get; set; }

        public Task<HistoryFeedResult> GetHistoryAsync(string? continuationToken = null,
            CancellationToken cancellationToken = default)
        {
            LastContinuationToken = continuationToken;
            return ResponseFactory?.Invoke(continuationToken, cancellationToken)
                   ?? Task.FromResult(new HistoryFeedResult([], null, true, "OK", false));
        }
    }
}
