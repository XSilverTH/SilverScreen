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

using System.Net;

namespace SilverScreen.Tests.Player;

public sealed class SponsorBlockServiceTests
{
    [Fact]
    public async Task GetSegmentsAsync_MapsSelectedSkipSegments_AndCachesTheRequest()
    {
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(
                "https://sponsor.ajay.app/api/skipSegments?videoID=dQw4w9WgXcQ&actionType=skip&category=sponsor&category=outro",
                request.RequestUri!.AbsoluteUri);
            return Task.FromResult(JsonResponse("""
                                                [
                                                  { "segment": [12.5, 36], "UUID": "sponsor-id", "category": "sponsor", "actionType": "skip" },
                                                  { "segment": [4, 8], "UUID": "intro-id", "category": "intro", "actionType": "skip" },
                                                  { "segment": [40, 50], "UUID": "mute-id", "category": "outro", "actionType": "mute" },
                                                  { "segment": [75, 74], "UUID": "invalid-id", "category": "outro", "actionType": "skip" }
                                                ]
                                                """));
        });
        using var client = new HttpClient(handler);
        using var service = new SponsorBlockService(client);

        var categories = new[] { SponsorBlockCategories.Sponsor, SponsorBlockCategories.Outro };
        var first = await service.GetSegmentsAsync("dQw4w9WgXcQ", categories);
        var cached = await service.GetSegmentsAsync("dQw4w9WgXcQ", categories);

        var segment = Assert.Single(first);
        Assert.Equal("sponsor-id", segment.Id);
        Assert.Equal(TimeSpan.FromSeconds(12.5), segment.Start);
        Assert.Equal(TimeSpan.FromSeconds(36), segment.End);
        Assert.Equal(SponsorBlockCategories.Sponsor, segment.Category);
        Assert.Same(first, cached);
        Assert.Equal(1, handler.CallCount);
    }


    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
    }

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return handler(request, cancellationToken);
        }
    }
}
