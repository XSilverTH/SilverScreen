using SilverScreen.Account.Auth;
using Xunit;

namespace SilverScreen.Tests.Account.Auth;

public sealed class WebLoginAllowedHostTests
{
    [Theory]
    [InlineData("accounts.google.com")]
    [InlineData("myaccount.google.com")]
    [InlineData("policies.google.com")]
    [InlineData("policies.google")]
    [InlineData("support.google.com")]
    [InlineData("passkeys.google.com")]
    [InlineData("google.com")]
    [InlineData("www.google.com")]
    [InlineData("accounts.google.co.uk")]
    [InlineData("myaccount.google.de")]
    [InlineData("policies.google.fr")]
    [InlineData("accounts.google.ca")]
    [InlineData("accounts.google.co.jp")]
    [InlineData("accounts.google.com.au")]
    [InlineData("myaccount.google.com.br")]
    [InlineData("google.co.uk")]
    [InlineData("google.de")]
    [InlineData("youtube.com")]
    [InlineData("www.youtube.com")]
    [InlineData("accounts.youtube.com")]
    [InlineData("m.youtube.com")]
    [InlineData("youtube.co.uk")]
    [InlineData("youtube.de")]
    [InlineData("accounts.google.com.")]
    public void IsAllowedHost_AllowedHosts_ReturnsTrue(string host)
    {
        Assert.True(WebLoginWindow.IsAllowedHost(host));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("attacker.com")]
    [InlineData("google.attacker.com")]
    [InlineData("accounts.google.attacker.com")]
    [InlineData("myaccount.google.phishing.org")]
    [InlineData("evilgoogle.com")]
    [InlineData("notgoogle.com")]
    [InlineData("fake-youtube.com")]
    [InlineData("youtube.com.attacker.com")]
    [InlineData("attacker.google.com.evil.com")]
    [InlineData("google.com.evil")]
    public void IsAllowedHost_BlockedHosts_ReturnsFalse(string? host)
    {
        Assert.False(WebLoginWindow.IsAllowedHost(host!));
    }
}
