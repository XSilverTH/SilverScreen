using System.Net;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Player;

namespace SilverScreen.Tests.Player;

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

    [Fact]
    public void FreshPreferences_RydIsOffByDefault()
    {
        Assert.False(new AppPreferences().RydEnabled);
    }

    [Fact]
    public async Task GetEngagementAsync_WhenDisabled_ReturnsNullWithoutHttp()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(JsonResponse("{}")));
        using var client = new HttpClient(handler);
        var preferences = new FakePreferencesService(new AppPreferences { RydEnabled = false });
        using var service = new ReturnYouTubeDislikeService(client, preferences);

        Assert.Null(await service.GetEngagementAsync("dQw4w9WgXcQ"));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task GetEngagementAsync_WhenStoredTrueIsHonored_FetchesCounts()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(JsonResponse("""
                                                { "id": "dQw4w9WgXcQ", "likes": 10, "dislikes": 3 }
                                                """)));
        using var client = new HttpClient(handler);
        var preferences = new FakePreferencesService(new AppPreferences { RydEnabled = true });
        using var service = new ReturnYouTubeDislikeService(client, preferences);

        var engagement = await service.GetEngagementAsync("dQw4w9WgXcQ");

        Assert.Equal(10, engagement?.Likes);
        Assert.Equal(3, engagement?.Dislikes);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetEngagementAsync_WhenDisabledAfterCaching_ReturnsNullAndDropsStale()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(JsonResponse("""
                                                { "id": "dQw4w9WgXcQ", "likes": 10, "dislikes": 3 }
                                                """)));
        using var client = new HttpClient(handler);
        var preferences = new FakePreferencesService(new AppPreferences { RydEnabled = true });
        using var service = new ReturnYouTubeDislikeService(client, preferences);

        Assert.NotNull(await service.GetEngagementAsync("dQw4w9WgXcQ"));
        Assert.Equal(1, handler.CallCount);

        preferences.Current = new AppPreferences { RydEnabled = false };
        Assert.Null(await service.GetEngagementAsync("dQw4w9WgXcQ"));
        Assert.Equal(1, handler.CallCount);

        preferences.Current = new AppPreferences { RydEnabled = true };
        Assert.NotNull(await service.GetEngagementAsync("dQw4w9WgXcQ"));
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetEngagementAsync_WhenPreferencesUnreadable_FailsClosedWithoutHttp()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(JsonResponse("{}")));
        using var client = new HttpClient(handler);
        var preferences = new FakePreferencesService(new AppPreferences { RydEnabled = true }) { ThrowOnRead = true };
        using var service = new ReturnYouTubeDislikeService(client, preferences);

        Assert.Null(await service.GetEngagementAsync("dQw4w9WgXcQ"));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task GetEngagementAsync_WhenServerErrors_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        using var client = new HttpClient(handler);
        var preferences = new FakePreferencesService(new AppPreferences { RydEnabled = true });
        using var service = new ReturnYouTubeDislikeService(client, preferences);

        Assert.Null(await service.GetEngagementAsync("dQw4w9WgXcQ"));
    }

    [Fact]
    public async Task GetEngagementAsync_EvictsOldestInsertedPastBound()
    {
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            var query = request.RequestUri!.Query;
            var id = query.Substring(query.IndexOf('=') + 1);
            return Task.FromResult(JsonResponse($"{{\"id\": \"{id}\", \"likes\": 1, \"dislikes\": 2}}"));
        });
        using var client = new HttpClient(handler);
        var preferences = new FakePreferencesService(new AppPreferences { RydEnabled = true });
        using var service = new ReturnYouTubeDislikeService(client, preferences);

        var ids = Enumerable.Range(0, ReturnYouTubeDislikeService.MaxCachedVideos + 1)
            .Select(i => $"dQw4w9Wg{i:D3}")
            .ToArray();
        foreach (var id in ids)
            Assert.NotNull(await service.GetEngagementAsync(id));
        Assert.Equal(ids.Length, handler.CallCount);

        Assert.NotNull(await service.GetEngagementAsync(ids[0]));
        Assert.Equal(ids.Length + 1, handler.CallCount);

        Assert.NotNull(await service.GetEngagementAsync(ids[^1]));
        Assert.Equal(ids.Length + 1, handler.CallCount);
    }

    private sealed class FakePreferencesService(AppPreferences initial) : IPreferencesService
    {
        public AppPreferences Current { get; set; } = initial;
        public bool ThrowOnRead { get; init; }
        public event EventHandler<AppPreferences>? PreferencesChanged;

        public AppPreferences GetPreferences()
        {
            return ThrowOnRead ? throw new InvalidOperationException("preferences unreadable") : Current;
        }

        public void SavePreferences(AppPreferences preferences)
        {
            Current = preferences;
            PreferencesChanged?.Invoke(this, preferences);
        }
    }

}