using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Features.Feed;
using SilverScreen.Infrastructure.Features.Session;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests;

public sealed class AuthenticatedHomeFeedServiceTests
{
    private const string CookieContent =
        "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t2147483647\tSID\tsession\n";

    [Fact]
    public async Task LoadFirstPageAsync_WithoutSession_RequiresAuthenticationWithoutCallingClient()
    {
        var client = new FakeYouTubeHomeClient();
        var service = new AuthenticatedHomeFeedService(client, new InMemorySessionService());

        var result = await service.LoadFirstPageAsync();

        Assert.Equal(AuthenticatedHomeFeedStatus.AuthenticationRequired, result.Status);
        Assert.Equal(0, client.CallCount);
    }

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
        public int CallCount { get; private set; }
        public string? LastContinuationToken { get; private set; }
        public Func<string?, CancellationToken, Task<HomeFeedResult>>? ResponseFactory { get; set; }

        public Task<HomeFeedResult> GetHomeFeedAsync(string? continuationToken = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastContinuationToken = continuationToken;
            return ResponseFactory?.Invoke(continuationToken, cancellationToken)
                   ?? Task.FromResult(new HomeFeedResult([], null, true, "OK", false));
        }
    }
}