using SilverScreen.Features.Session;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests;

public sealed class WebLoginCookieTests
{
    [Fact]
    public void SerializeNetscape_OmitsOtherDomainsAndRejectsMalformedFields()
    {
        var omitted = WebLoginCookieReader.SerializeNetscape(
            [new WebCookieSnapshot("SID", "google-value", ".google.com", "/", true, false, 0)]);
        Assert.Equal("# Netscape HTTP Cookie File\n", omitted);

        Assert.Throws<ArgumentException>(() => WebLoginCookieReader.SerializeNetscape(
            [new WebCookieSnapshot("S\tID", "value", ".youtube.com", "/", true, false, 0)]));
        Assert.Throws<ArgumentException>(() => WebLoginCookieReader.SerializeNetscape(
            [new WebCookieSnapshot("SID", "line\nbreak", ".youtube.com", "/", true, false, 0)]));
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