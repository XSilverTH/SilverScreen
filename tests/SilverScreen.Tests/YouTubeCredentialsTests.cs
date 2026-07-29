using System.Text;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests;

public sealed class YouTubeCredentialsTests
{
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

    private static string CreateNetscapeCookieFile(params (string Name, string Value)[] cookies)
    {
        var content = new StringBuilder("# Netscape HTTP Cookie File\n");
        foreach (var (name, value) in cookies)
            content.AppendLine($"youtube.com\tTRUE\t/\tTRUE\t2147483647\t{name}\t{value}");

        return content.ToString();
    }
}