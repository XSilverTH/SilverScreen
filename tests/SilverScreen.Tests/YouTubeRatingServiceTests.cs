using System.Net;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Engagement;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests;

public sealed class YouTubeRatingServiceTests
{
    [Fact]
    public async Task SubmitVoteAsync_WithAuthenticatedSession_BootstrapsAndPostsNativeLike()
    {
        var handler = new FakeHttpMessageHandler(async (request, _) =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/watch")
                return HtmlResponse(""" { "likeStatus": "INDIFFERENT", "likeParams": "like-token" } """);

            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal("https://www.youtube.com/", request.RequestUri!.AbsoluteUri);
                Assert.DoesNotContain("X-Goog-AuthUser", request.Headers.SelectMany(pair => pair.Value));
                return HtmlResponse(
                    """ { "INNERTUBE_API_KEY": "test-key", "INNERTUBE_CLIENT_VERSION": "2.20260724.01.00", "VISITOR_DATA": "visitor" } """);
            }

            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://www.youtube.com/youtubei/v1/like/like?key=test-key&prettyPrint=false",
                request.RequestUri!.AbsoluteUri);
            Assert.StartsWith("SAPISIDHASH ", request.Headers.Authorization!.ToString());
            Assert.Contains("SAPISID=sapisid", request.Headers.GetValues("Cookie").Single());
            Assert.Contains("true", request.Headers.GetValues("X-Youtube-Bootstrap-Logged-In"));
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("\"target\":{\"videoId\":\"dQw4w9WgXcQ\"}", body);
            Assert.Contains("\"clientName\":\"WEB\"", body);
            Assert.Contains("\"params\":\"like-token\"", body);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var session = new SignedInSessionService();
        using var authentication = new YouTubeAuthenticationService(session)
        { TimeSource = () => 1700000000L };
        using var client = new HttpClient(handler);
        using var service = new YouTubeRatingService(client, authentication);

        var submitted = await service.SubmitVoteAsync("dQw4w9WgXcQ", VideoVote.Like);

        Assert.True(submitted);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task GetRatingStateAsync_ParsesTheAuthenticatedYouTubeRating()
    {
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", request.RequestUri!.AbsoluteUri);
            Assert.Contains("SAPISID=sapisid", request.Headers.GetValues("Cookie").Single());
            Assert.Contains("0", request.Headers.GetValues("X-Goog-AuthUser"));
            return Task.FromResult(HtmlResponse("{ \\\"likeStatus\\\": \\\"DISLIKE\\\" }"));
        });
        using var session = new SignedInSessionService();
        using var authentication = new YouTubeAuthenticationService(session);
        using var client = new HttpClient(handler);
        using var service = new YouTubeRatingService(client, authentication);

        var state = await service.GetRatingStateAsync("dQw4w9WgXcQ");

        Assert.Equal(YouTubeRatingState.Dislike, state);
    }

    [Fact]
    public async Task SessionChange_ClearsCachedRatingMetadata()
    {
        var handler = new FakeHttpMessageHandler((request, _) =>
            Task.FromResult(HtmlResponse(""" { "likeStatus": "LIKE" } """)));
        using var session = new SignedInSessionService();
        using var authentication = new YouTubeAuthenticationService(session);
        using var client = new HttpClient(handler);
        using var service = new YouTubeRatingService(client, authentication);

        await service.GetRatingStateAsync("dQw4w9WgXcQ");
        session.SetManualSession("changed", SessionCookieFormat.NetscapeCookiesText);
        await service.GetRatingStateAsync("dQw4w9WgXcQ");

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task RemoveVoteAsync_WithAuthenticatedSession_PostsTheNativeRemovalAction()
    {
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/watch")
                return Task.FromResult(
                    HtmlResponse(""" { "likeStatus": "LIKE", "removeLikeParams": "remove-token" } """));

            if (request.Method == HttpMethod.Get)
                return Task.FromResult(HtmlResponse(
                    """ { "INNERTUBE_API_KEY": "test-key", "INNERTUBE_CLIENT_VERSION": "2.20260724.01.00" } """));

            Assert.Equal("https://www.youtube.com/youtubei/v1/like/removelike?key=test-key&prettyPrint=false",
                request.RequestUri!.AbsoluteUri);
            Assert.Contains("\"params\":\"remove-token\"", request.Content!.ReadAsStringAsync().Result);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var session = new SignedInSessionService();
        using var authentication = new YouTubeAuthenticationService(session);
        using var client = new HttpClient(handler);
        using var service = new YouTubeRatingService(client, authentication);

        var removed = await service.RemoveVoteAsync("dQw4w9WgXcQ", VideoVote.Like);

        Assert.True(removed);
    }


    private static HttpResponseMessage HtmlResponse(string html)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) };
    }

    private sealed class SignedInSessionService : ISessionService, IDisposable
    {
        private const string Cookies = ".youtube.com\tTRUE\t/\tTRUE\t0\tSAPISID\tsapisid";

        public void Dispose()
        {
        }

        public event EventHandler? SessionChanged;

        public AccountSession GetCurrentSession()
        {
            return new AccountSession(true, HasManualSession: true);
        }

        public ManualSessionCookies? GetManualSessionCookies()
        {
            return new ManualSessionCookies(SessionCookieFormat.NetscapeCookiesText, Cookies);
        }

        public void SetManualSession(string cookieContent, SessionCookieFormat format)
        {
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearSession()
        {
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
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