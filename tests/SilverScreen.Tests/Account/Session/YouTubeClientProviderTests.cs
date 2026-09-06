using SilverScreen.Core.Account.Session;
using SilverScreen.Infrastructure.Account.Session;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests.Account.Session;

public sealed class YouTubeClientProviderTests
{
    [Fact]
    public void GetClient_WithCorruptedSessionCookies_DoesNotThrowAndReturnsClient()
    {
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession("corrupted_cookie_content_not_valid_netscape", SessionCookieFormat.NetscapeCookiesText);

        using var provider = new YouTubeClientProvider(sessionService);

        var client = provider.GetClient();

        Assert.NotNull(client);
    }

    [Fact]
    public void GetClient_WithValidSessionCookies_ReturnsClient()
    {
        var sessionService = new InMemorySessionService();
        var futureExpiry = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        var validCookies = $".youtube.com\tTRUE\t/\tTRUE\t{futureExpiry}\tSAPISID\tauth_sapisid_value\n";
        sessionService.SetManualSession(validCookies, SessionCookieFormat.NetscapeCookiesText);

        using var provider = new YouTubeClientProvider(sessionService);

        var client = provider.GetClient();

        Assert.NotNull(client);
    }

    [Fact]
    public void GetClient_WithoutSession_ReturnsClient()
    {
        var sessionService = new InMemorySessionService();
        using var provider = new YouTubeClientProvider(sessionService);

        var client = provider.GetClient();

        Assert.NotNull(client);
    }
}
