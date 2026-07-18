using SilverScreen.Features.Session;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests;

public sealed class WebLoginCookieTests
{
    [Fact]
    public void SerializeNetscape_EmitsHttpOnlyAndNormalCookiesExactly()
    {
        var cookies = new[]
        {
            new WebCookieSnapshot("SID", "sid-value", ".youtube.com", "/", true, false, 2_147_483_647),
            new WebCookieSnapshot("__Secure-3PAPISID", "secure-value", ".YouTube.COM", "/", true, true,
                2_000_000_000)
        };

        var result = WebLoginCookieReader.SerializeNetscape(cookies);

        Assert.Equal(
            "# Netscape HTTP Cookie File\n" +
            ".youtube.com\tTRUE\t/\tTRUE\t2147483647\tSID\tsid-value\n" +
            "#HttpOnly_.youtube.com\tTRUE\t/\tTRUE\t2000000000\t__Secure-3PAPISID\tsecure-value\n",
            result);
    }

    [Fact]
    public void SerializeNetscape_UsesSessionExpiryHostOnlyFlagAndLastDuplicate()
    {
        var cookies = new[]
        {
            new WebCookieSnapshot("SAPISID", "first", "youtube.com", "", true, false, -1),
            new WebCookieSnapshot("SAPISID", "last", "YouTube.com", "/", true, false, 0)
        };

        var result = WebLoginCookieReader.SerializeNetscape(cookies);

        Assert.Equal(
            "# Netscape HTTP Cookie File\n" +
            "youtube.com\tFALSE\t/\tTRUE\t0\tSAPISID\tlast\n",
            result);
    }

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
