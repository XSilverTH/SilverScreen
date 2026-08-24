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
using System.Text;

namespace SilverScreen.Tests.Account.Profile;

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
            using var authentication = new YouTubeAuthenticationService(session);
            authentication.TimeSource = () => 1700000000L;
            using var service = new YouTubeAccountProfileService(httpClient, session, authentication, cachePath);

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
            using (var authentication = new YouTubeAuthenticationService(session))
            using (var service = new YouTubeAccountProfileService(httpClient, session, authentication, cachePath))
            {
                Assert.Equal(expected, await service.GetCurrentProfileAsync());
                Assert.Equal(expected, service.GetCachedProfile());
            }

            var failingHandler =
                new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            using var failingHttpClient = new HttpClient(failingHandler);
            using var refreshedAuthentication = new YouTubeAuthenticationService(session);
            using var refreshedService = new YouTubeAccountProfileService(failingHttpClient, session,
                refreshedAuthentication, cachePath);

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
            using var authentication = new YouTubeAuthenticationService(session);
            using var service = new YouTubeAccountProfileService(httpClient, session, authentication, cachePath);

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

    private static string CreateTemporaryCachePath()
    {
        return Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.account-profile.json");
    }

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
        private int CallCount { get; set; }
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
