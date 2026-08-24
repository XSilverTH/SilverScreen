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

namespace SilverScreen.Tests.Account.Auth;

public sealed class YouTubeAuthenticationServiceTests
{
    private const string BootstrapHtml = """
                                         { "INNERTUBE_API_KEY": "key", "INNERTUBE_CONTEXT_CLIENT_VERSION": "version", "VISITOR_DATA": "visitor" }
                                         """;


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
        builder.AppendLine($"youtube.com\tTRUE\t/\tTRUE\t2147483647\tSID\t{SapsidForSid(sapisid)}");
        return builder.ToString();

        static string SapsidForSid(string value)
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

        public ManualSessionCookies GetManualSessionCookies()
        {
            return new ManualSessionCookies(SessionCookieFormat.NetscapeCookiesText, _cookies);
        }
        public CookieFileLease? AcquireCookieFileLease()
        {
            return TemporaryCookieFile.CreateLease(_cookies);
        }

        public CookieFileLease? CreateCookieFile() => AcquireCookieFileLease();

        public System.Net.CookieContainer? CreateCookieContainer()
        {
            return NetscapeCookieParser.CreateCookieContainer(_cookies);
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
