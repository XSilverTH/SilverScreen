using SilverScreen.Core.Account.Session;

namespace SilverScreen.Infrastructure.Account.Session;

public sealed class TemporaryCookieFileProvider(ISessionService sessionService, string? tempRoot = null)
    : ICookieFileProvider
{
    private readonly string? _tempRoot = tempRoot;

    public CookieFileLease? CreateCookieFile()
    {
        var cookies = sessionService.GetManualSessionCookies();
        if (cookies is null || cookies.Format != SessionCookieFormat.NetscapeCookiesText ||
            string.IsNullOrWhiteSpace(cookies.Content))
            return null;

        return TemporaryCookieFile.CreateLease(cookies.Content, _tempRoot);
    }
}