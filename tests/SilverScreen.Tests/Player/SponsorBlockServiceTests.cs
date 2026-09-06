using System.Net;
using System.Security.Cryptography;
using System.Text;
using SilverScreen.Core.Player;
using SilverScreen.Infrastructure.Player;
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
                "https://sponsor.ajay.app/api/skipSegments/5f6b?actionType=skip&category=sponsor&category=outro",
                request.RequestUri!.AbsoluteUri);
            Assert.DoesNotContain("dQw4w9WgXcQ", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(JsonResponse("""
                                                [
                                                  {
                                                    "videoID": "dQw4w9WgXcQ",
                                                    "segments": [
                                                      { "segment": [12.5, 36], "UUID": "sponsor-id", "category": "sponsor", "actionType": "skip" },
                                                      { "segment": [4, 8], "UUID": "intro-id", "category": "intro", "actionType": "skip" },
                                                      { "segment": [40, 50], "UUID": "mute-id", "category": "outro", "actionType": "mute" },
                                                      { "segment": [75, 74], "UUID": "invalid-id", "category": "outro", "actionType": "skip" }
                                                    ]
                                                  },
                                                  {
                                                    "videoID": "other-video-same-prefix",
                                                    "segments": [
                                                      { "segment": [1, 5], "UUID": "other-id", "category": "sponsor", "actionType": "skip" }
                                                    ]
                                                  }
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
    public async Task GetSegmentsAsync_EvictsOldestInsertedPastBound()
    {
        var ids = Enumerable.Range(0, SponsorBlockService.MaxCachedRequests + 1)
            .Select(i => $"dQw4w9Wg{i:D3}")
            .ToArray();

        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            var prefix = request.RequestUri!.AbsolutePath.Split('/')[^1];
            var matchingIds = ids.Where(id =>
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id))).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

            var json = $$"""
                        [
                          {{string.Join(",\n", matchingIds.Select(id => $$"""
                          {
                            "videoID": "{{id}}",
                            "segments": [
                              { "segment": [1, 2], "UUID": "id-{{id}}", "category": "sponsor", "actionType": "skip" }
                            ]
                          }
                          """))}}
                        ]
                        """;
            return Task.FromResult(JsonResponse(json));
        });
        using var client = new HttpClient(handler);
        using var service = new SponsorBlockService(client);

        var categories = new[] { SponsorBlockCategories.Sponsor };
        foreach (var id in ids)
            Assert.NotEmpty(await service.GetSegmentsAsync(id, categories));
        Assert.Equal(ids.Length, handler.CallCount);

        Assert.NotEmpty(await service.GetSegmentsAsync(ids[0], categories));
        Assert.Equal(ids.Length + 1, handler.CallCount);

        Assert.NotEmpty(await service.GetSegmentsAsync(ids[^1], categories));
        Assert.Equal(ids.Length + 1, handler.CallCount);
    }
    [Fact]
    public async Task GetSegmentsAsync_WhenNotFound_ReturnsEmpty()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var client = new HttpClient(handler);
        using var service = new SponsorBlockService(client);

        var segments = await service.GetSegmentsAsync("dQw4w9WgXcQ", [SponsorBlockCategories.Sponsor]);
        Assert.Empty(segments);
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