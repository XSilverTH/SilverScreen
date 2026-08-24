using System.Net;

namespace SilverScreen.Core.Account.Session;

public interface ISessionService : ICookieFileProvider
{
    CookieFileLease? ICookieFileProvider.CreateCookieFile()
    {
        return AcquireCookieFileLease();
    }

    event EventHandler? SessionChanged;

    AccountSession GetCurrentSession();

    ManualSessionCookies? GetManualSessionCookies();

    void SetManualSession(string cookieContent, SessionCookieFormat format);

    void ClearSession();

    CookieFileLease? AcquireCookieFileLease();

    CookieContainer? CreateCookieContainer();
}

public sealed record ManualSessionCookies(SessionCookieFormat Format, string Content);