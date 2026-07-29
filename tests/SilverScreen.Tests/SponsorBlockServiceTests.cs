using System.Net;
using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Features.Playback;

namespace SilverScreen.Tests;

public sealed class SponsorBlockServiceTests
{
    [Theory]
    [InlineData(SponsorBlockCategories.Sponsor, "#00d400", 0, 212, 0, 0.7)]
    [InlineData(SponsorBlockCategories.SelfPromotion, "#ffff00", 255, 255, 0, 0.7)]
    [InlineData(SponsorBlockCategories.InteractionReminder, "#cc00ff", 204, 0, 255, 0.7)]
    [InlineData(SponsorBlockCategories.Intro, "#00ffff", 0, 255, 255, 0.7)]
    [InlineData(SponsorBlockCategories.Outro, "#0202ed", 2, 2, 237, 0.7)]
    [InlineData(SponsorBlockCategories.Preview, "#008fd6", 0, 143, 214, 0.7)]
    [InlineData(SponsorBlockCategories.Hook, "#395699", 57, 86, 153, 0.8)]
    [InlineData(SponsorBlockCategories.Filler, "#7300FF", 115, 0, 255, 0.9)]
    public void GetColor_UsesOfficialSponsorBlockDefaults(string category, string hex, byte red, byte green,
        byte blue, double opacity)
    {
        Assert.Equal(new SponsorBlockCategoryColor(hex, red, green, blue, opacity),
            SponsorBlockCategories.GetColor(category));
    }

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

    [Fact]
    public async Task GetSegmentsAsync_InvalidVideoIdOrCategories_DoesNotCallTheApi()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new InvalidOperationException());
        using var client = new HttpClient(handler);
        using var service = new SponsorBlockService(client);

        var invalidVideo = await service.GetSegmentsAsync("not-a-youtube-id", SponsorBlockCategories.All);
        var invalidCategories = await service.GetSegmentsAsync("dQw4w9WgXcQ", ["unknown"]);

        Assert.Empty(invalidVideo);
        Assert.Empty(invalidCategories);
        Assert.Equal(0, handler.CallCount);
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