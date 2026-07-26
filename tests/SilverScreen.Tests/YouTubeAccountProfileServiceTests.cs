using System.Net;
using System.Text;
using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Features.Session;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests;

public sealed class YouTubeAccountProfileServiceTests
{
    private const string BootstrapHtml = """
                                         <script>
                                         var ytcfg = {
                                           "INNERTUBE_API_KEY": "fake-api-key",
                                           "INNERTUBE_CONTEXT_CLIENT_VERSION": "1.20260710.01.00"
                                         };
                                         </script>
                                         """;

    [Fact]
    public async Task GetCurrentProfileAsync_ExtractsAccountDetailsFromAccountMenu()
    {
        var session = new InMemorySessionService();
        session.SetManualSession(CreateNetscapeCookieFile(("SAPISID", "fake-sapisid")),
            SessionCookieFormat.NetscapeCookiesText);
        var handler = new FakeHttpMessageHandler(request => request.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(BootstrapHtml) }
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                                            {
                                              "actions": [{
                                                "accountItem": {
                                                  "accountName": { "simpleText": "Silver" },
                                                  "accountPhoto": {
                                                    "thumbnails": [
                                                      { "url": "https://example.com/small.png" },
                                                      { "url": "https://example.com/avatar.png" }
                                                    ]
                                                  }
                                                }
                                              }]
                                            }
                                            """)
            });
        var cachePath = CreateTemporaryCachePath();
        try
        {
            using var httpClient = new HttpClient(handler);
            using var service = new YouTubeAccountProfileService(httpClient, session, cachePath)
                { TimeSource = () => 1700000000L };

            var profile = await service.GetCurrentProfileAsync();

            Assert.Equal(new AccountProfile("Silver", "https://example.com/avatar.png"), profile);
            Assert.Equal("SAPISIDHASH", handler.PostAuthorizationScheme);
        }
        finally
        {
            File.Delete(cachePath);
        }
    }


    [Fact]
    public async Task GetCurrentProfileAsync_WithoutSessionDoesNotMakeHttpRequests()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        using var service = new YouTubeAccountProfileService(httpClient, new InMemorySessionService(),
            CreateTemporaryCachePath());

        var profile = await service.GetCurrentProfileAsync();

        Assert.Null(profile);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task GetCurrentProfileAsync_CachesProfileAndPreservesItAfterRefreshFailure()
    {
        var cachePath = CreateTemporaryCachePath();
        var session = new InMemorySessionService();
        session.SetManualSession(CreateNetscapeCookieFile(("SAPISID", "fake-sapisid")),
            SessionCookieFormat.NetscapeCookiesText);
        var expected = new AccountProfile("Silver", "https://example.com/avatar.png");
        try
        {
            var successfulHandler = new FakeHttpMessageHandler(CreateProfileResponse);
            using (var httpClient = new HttpClient(successfulHandler))
            using (var service = new YouTubeAccountProfileService(httpClient, session, cachePath))
            {
                Assert.Equal(expected, await service.GetCurrentProfileAsync());
                Assert.Equal(expected, service.GetCachedProfile());
            }

            var failingHandler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            using var failingHttpClient = new HttpClient(failingHandler);
            using var refreshedService = new YouTubeAccountProfileService(failingHttpClient, session, cachePath);

            Assert.Equal(expected, refreshedService.GetCachedProfile());
            Assert.Null(await refreshedService.GetCurrentProfileAsync());
            Assert.Equal(expected, refreshedService.GetCachedProfile());
        }
        finally
        {
            File.Delete(cachePath);
        }
    }

    [Fact]
    public void SessionChange_ClearsCachedProfile()
    {
        var cachePath = CreateTemporaryCachePath();
        File.WriteAllText(cachePath, """{"displayName":"Silver","avatarUrl":"https://example.com/avatar.png"}""");
        var session = new InMemorySessionService();
        session.SetManualSession(CreateNetscapeCookieFile(("SAPISID", "fake-sapisid")),
            SessionCookieFormat.NetscapeCookiesText);
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        try
        {
            using var httpClient = new HttpClient(handler);
            using var service = new YouTubeAccountProfileService(httpClient, session, cachePath);

            Assert.Equal(new AccountProfile("Silver", "https://example.com/avatar.png"), service.GetCachedProfile());

            session.ClearSession();

            Assert.Null(service.GetCachedProfile());
            Assert.False(File.Exists(cachePath));
        }
        finally
        {
            File.Delete(cachePath);
        }
    }

    private static string CreateTemporaryCachePath() =>
        Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.account-profile.json");

    private static HttpResponseMessage CreateProfileResponse(HttpRequestMessage request)
    {
        return request.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(BootstrapHtml) }
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                                            {
                                              "actions": [{
                                                "accountItem": {
                                                  "accountName": { "simpleText": "Silver" },
                                                  "accountPhoto": {
                                                    "thumbnails": [{ "url": "https://example.com/avatar.png" }]
                                                  }
                                                }
                                              }]
                                            }
                                            """)
            };
    }

    private static string CreateNetscapeCookieFile(params (string Name, string Value)[] cookies)
    {
        var content = new StringBuilder("# Netscape HTTP Cookie File\n");
        foreach (var (name, value) in cookies)
            content.AppendLine($"youtube.com\tTRUE\t/\tTRUE\t2147483647\t{name}\t{value}");

        return content.ToString();
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? PostAuthorizationScheme { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (request.Method == HttpMethod.Post)
                PostAuthorizationScheme = request.Headers.Authorization?.Scheme;

            return Task.FromResult(response(request));
        }
    }
}
