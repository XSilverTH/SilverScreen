using SilverScreen.Account.Auth;
using YoutubeAPI;

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

        var credentials = YouTubeCookieAuthentication.FromNetscape(
            WebLoginCookieReader.SerializeNetscape(cookies));

        Assert.NotNull(credentials);
        Assert.Contains(credentials.Cookies, cookie =>
            cookie.Name == "__Secure-3PAPISID" && cookie.Value == "secure-sapisid");
        Assert.Contains(credentials.Cookies, cookie => cookie.Name == "SID" && cookie.Value == "sid");
        Assert.Contains(credentials.Cookies, cookie => cookie.Name == "SAPISID" && cookie.Value == "sapisid");
    }
}