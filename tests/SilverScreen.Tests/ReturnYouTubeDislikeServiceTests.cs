using System.Net;
using SilverScreen.Infrastructure.Features.Engagement;

namespace SilverScreen.Tests;

public sealed class ReturnYouTubeDislikeServiceTests
{
    [Fact]
    public async Task GetEngagementAsync_MapsLiveCountsAndCachesTheVideo()
    {
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://returnyoutubedislikeapi.com/votes?videoId=dQw4w9WgXcQ",
                request.RequestUri!.AbsoluteUri);
            return Task.FromResult(JsonResponse("""
                                                { "id": "dQw4w9WgXcQ", "likes": 19270043, "dislikes": 515621 }
                                                """));
        });
        using var client = new HttpClient(handler);
        using var service = new ReturnYouTubeDislikeService(client);

        var first = await service.GetEngagementAsync("dQw4w9WgXcQ");
        var cached = await service.GetEngagementAsync("dQw4w9WgXcQ");

        Assert.Equal(19_270_043, first?.Likes);
        Assert.Equal(515_621, first?.Dislikes);
        Assert.Equal(first, cached);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetEngagementAsync_InvalidVideoId_DoesNotCallTheApi()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new InvalidOperationException());
        using var client = new HttpClient(handler);
        using var service = new ReturnYouTubeDislikeService(client);

        var engagement = await service.GetEngagementAsync("not-a-youtube-id");

        Assert.Null(engagement);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task GetEngagementAsync_ApiFailure_ReturnsNoCounts()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        using var client = new HttpClient(handler);
        using var service = new ReturnYouTubeDislikeService(client);

        var engagement = await service.GetEngagementAsync("dQw4w9WgXcQ");

        Assert.Null(engagement);
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