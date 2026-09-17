using SilverScreen.Account.Auth;
using Xunit;

namespace SilverScreen.Tests.Account.Auth;

public sealed class WebLoginAllowedHostTests
{
    [Theory]
    [InlineData("accounts.google.com")]
    [InlineData("accounts.google.com.")]
    [InlineData("youtube.com")]
    [InlineData("www.youtube.com")]
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
    [InlineData("myaccount.google.com")]
    [InlineData("policies.google.com")]
    [InlineData("support.google.com")]
    [InlineData("passkeys.google.com")]
    [InlineData("google.com")]
    [InlineData("www.google.com")]
    [InlineData("accounts.youtube.com")]
    [InlineData("m.youtube.com")]
    public void IsAllowedHost_BlockedHosts_ReturnsFalse(string? host)
    {
        Assert.False(WebLoginWindow.IsAllowedHost(host!));
    }
}
