using System.Net;
using System.Text;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests;

public sealed class YouTubeAuthenticationServiceTests
{
    private const string BootstrapHtml = """
                                         { "INNERTUBE_API_KEY": "key", "INNERTUBE_CONTEXT_CLIENT_VERSION": "version", "VISITOR_DATA": "visitor" }
                                         """;

    [Fact]
    public void GetCurrentCredentials_DoesNotIssueBootstrapHttp()
    {
        var session = new MutableSession(CreateCookies("first"));
        var handler = new RecordingHandler(_ => Task.FromResult(BootstrapResponse()));
        using var client = new HttpClient(handler);
        using var authentication = new YouTubeAuthenticationService(session);

        var credentials = authentication.GetCurrentCredentials();

        Assert.NotNull(credentials);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetCurrentAsync_ReusesOneBootstrapForStableSession()
    {
        var session = new MutableSession(CreateCookies("first"));
        var handler = new RecordingHandler(_ => Task.FromResult(BootstrapResponse()));
        using var client = new HttpClient(handler);
        using var authentication = new YouTubeAuthenticationService(session);

        var first = await authentication.GetCurrentAsync(client, false);
        var second = await authentication.GetCurrentAsync(client, true);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SessionChanged_RefetchesBootstrapWithNewCookie()
    {
        var session = new MutableSession(CreateCookies("first"));
        var handler = new RecordingHandler(_ => Task.FromResult(BootstrapResponse()));
        using var client = new HttpClient(handler);
        using var authentication = new YouTubeAuthenticationService(session);

        Assert.NotNull(await authentication.GetCurrentAsync(client, false));
        session.SetCookies(CreateCookies("second"));
        var current = await authentication.GetCurrentAsync(client, false);

        Assert.NotNull(current);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("SAPISID=second", handler.Requests[1].Headers.GetValues("Cookie").Single());
        Assert.NotEqual(handler.Requests[0].Headers.GetValues("Cookie").Single(),
            handler.Requests[1].Headers.GetValues("Cookie").Single());
    }

    [Fact]
    public async Task HeaderPaths_KeepWatchAndAuthenticatedDifferences()
    {
        var session = new MutableSession(CreateCookies("first"));
        var handler = new RecordingHandler(_ => Task.FromResult(BootstrapResponse()));
        using var client = new HttpClient(handler);
        using var authentication = new YouTubeAuthenticationService(session, new YouTubeWebOptions { AuthUser = 2 })
            { TimeSource = () => 1700000000L };
        var authenticated = await authentication.GetCurrentAsync(client, true);
        Assert.NotNull(authenticated);
        var credentials = authenticated!.CredentialSnapshot;

        using var watchRequest = new HttpRequestMessage(HttpMethod.Get, YouTubeWebOptions.Referer);
        authentication.ApplyWatchPageHeaders(watchRequest, credentials, true);
        using var accountRequest = new HttpRequestMessage(HttpMethod.Post, YouTubeWebOptions.Referer);
        authentication.ApplyAuthenticatedHeaders(accountRequest, authenticated, false);
        using var ratingRequest = new HttpRequestMessage(HttpMethod.Post, YouTubeWebOptions.Referer);
        authentication.ApplyAuthenticatedHeaders(ratingRequest, authenticated, true);

        Assert.Equal("2", watchRequest.Headers.GetValues("X-Goog-AuthUser").Single());
        Assert.Null(watchRequest.Headers.Authorization);
        Assert.False(accountRequest.Headers.Contains("X-Goog-AuthUser"));
        Assert.False(accountRequest.Headers.Contains("X-Youtube-Bootstrap-Logged-In"));
        Assert.Equal("2", ratingRequest.Headers.GetValues("X-Goog-AuthUser").Single());
        Assert.Equal("true", ratingRequest.Headers.GetValues("X-Youtube-Bootstrap-Logged-In").Single());
        Assert.StartsWith("SAPISIDHASH ", accountRequest.Headers.Authorization!.ToString());
        Assert.Equal(accountRequest.Headers.Authorization!.ToString(), ratingRequest.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task StaleBootstrapResult_IsNotReturnedOrCachedAfterSessionChange()
    {
        var session = new MutableSession(CreateCookies("first"));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHandler(async _ =>
        {
            started.TrySetResult();
            await release.Task;
            return BootstrapResponse();
        });
        using var client = new HttpClient(handler);
        using var authentication = new YouTubeAuthenticationService(session);

        var pending = authentication.GetCurrentAsync(client, false);
        await started.Task;
        session.SetCookies(CreateCookies("second"));
        release.SetResult();

        Assert.Null(await pending);
        Assert.NotNull(await authentication.GetCurrentAsync(client, false));
        Assert.Equal(2, handler.Requests.Count);
    }

    private static HttpResponseMessage BootstrapResponse()
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(BootstrapHtml) };
    }

    private static string CreateCookies(string sapisid)
    {
        var builder = new StringBuilder("# Netscape HTTP Cookie File\n");
        builder.AppendLine($"youtube.com\tTRUE\t/\tTRUE\t2147483647\tSAPISID\t{sapisid}");
        builder.AppendLine($"youtube.com\tTRUE\t/\tTRUE\t2147483647\tSID\t{sapsidForSid(sapisid)}");
        return builder.ToString();

        static string sapsidForSid(string value)
        {
            return $"sid-{value}";
        }
    }

    private sealed class MutableSession(string cookies) : ISessionService
    {
        private string _cookies = cookies;
        public event EventHandler? SessionChanged;

        public AccountSession GetCurrentSession()
        {
            return new AccountSession(true, HasManualSession: true);
        }

        public ManualSessionCookies? GetManualSessionCookies()
        {
            return new ManualSessionCookies(SessionCookieFormat.NetscapeCookiesText, _cookies);
        }

        public void SetManualSession(string cookieContent, SessionCookieFormat format)
        {
            _cookies = cookieContent;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearSession()
        {
            _cookies = string.Empty;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetCookies(string cookies)
        {
            SetManualSession(cookies, SessionCookieFormat.NetscapeCookiesText);
        }
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return await responder(request);
        }
    }
}