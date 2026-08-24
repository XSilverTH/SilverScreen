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


namespace SilverScreen.Tests.Account.Auth;

public sealed class WebLoginCookieTests
{
    [Fact]
    public void SerializeNetscape_OmitsOtherDomains()
    {
        var omitted = WebLoginCookieReader.SerializeNetscape(
            [new WebCookieSnapshot("SID", "google-value", ".google.com", "/", true, false, 0)]);
        Assert.Equal("# Netscape HTTP Cookie File\n", omitted);
    }

    [Fact]
    public void SerializeNetscape_ProducesCredentialsAcceptedByHomeAuthenticationPath()
    {
        var cookies = new[]
        {
            new WebCookieSnapshot("SID", "sid", ".youtube.com", "/", true, true, 2_147_483_647),
            new WebCookieSnapshot("HSID", "hsid", ".youtube.com", "/", true, true, 2_147_483_647),
            new WebCookieSnapshot("SAPISID", "sapisid", ".youtube.com", "/", true, true, 2_147_483_647),
            new WebCookieSnapshot("__Secure-3PAPISID", "secure-sapisid", ".youtube.com", "/", true, true,
                2_147_483_647)
        };

        var credentials = YouTubeCredentials.ParseNetscape(WebLoginCookieReader.SerializeNetscape(cookies));

        Assert.NotNull(credentials);
        Assert.Equal("secure-sapisid", credentials.Sapisid);
        Assert.Contains("SID=sid", credentials.CookieHeader, StringComparison.Ordinal);
        Assert.Contains("__Secure-3PAPISID=secure-sapisid", credentials.CookieHeader, StringComparison.Ordinal);
    }
}
