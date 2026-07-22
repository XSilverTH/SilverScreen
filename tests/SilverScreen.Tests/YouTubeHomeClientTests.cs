using System.Net;
using System.Text;
using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Features.Session;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests;

public sealed class YouTubeHomeClientTests
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
    public void GenerateSapisidHash_UsesKnownValue()
    {
        var credentials = YouTubeCredentials.ParseNetscape(CreateNetscapeCookieFile(("SAPISID", "fake-sapisid")));

        Assert.NotNull(credentials);
        Assert.Equal("6b2c32afdc7a2c4f00b844c84a58147e96fba5d6", credentials.GenerateSapisidHash(1700000000L));
    }

    [Fact]
    public void ParseNetscape_ExcludesUnrelatedCookiesFromTheAuthorizationHeader()
    {
        var credentials = YouTubeCredentials.ParseNetscape(CreateNetscapeCookieFile(
            ("SID", "sid"), ("SAPISID", "sapisid"), ("PREF", "unrelated")));

        Assert.NotNull(credentials);
        Assert.Contains("SID=sid", credentials.CookieHeader);
        Assert.DoesNotContain("PREF", credentials.CookieHeader);
    }

    [Fact]
    public async Task GetHomeFeedAsync_WithoutSessionDoesNotMakeHttpRequests()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        using var client = new YouTubeHomeClient(httpClient, new InMemorySessionService());

        var result = await client.GetHomeFeedAsync();

        Assert.True(result.RequiresAuthentication);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task GetHomeFeedAsync_SendsSapisidAuthorizationHeader()
    {
        var session = new InMemorySessionService();
        session.SetManualSession(CreateNetscapeCookieFile(("SAPISID", "fake-sapisid")),
            SessionCookieFormat.NetscapeCookiesText);
        var handler = new FakeHttpMessageHandler(request => request.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(BootstrapHtml) }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        using var httpClient = new HttpClient(handler);
        using var client = new YouTubeHomeClient(httpClient, session) { TimeSource = () => 1700000000L };

        var result = await client.GetHomeFeedAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("SAPISIDHASH", handler.PostAuthorizationScheme);
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