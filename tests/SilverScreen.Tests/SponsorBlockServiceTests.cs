using System.Net;
using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Features.Playback;

namespace SilverScreen.Tests;

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