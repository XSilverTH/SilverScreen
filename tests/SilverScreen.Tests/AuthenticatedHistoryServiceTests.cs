using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Features.Feed;
using SilverScreen.Infrastructure.Features.Session;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests;

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