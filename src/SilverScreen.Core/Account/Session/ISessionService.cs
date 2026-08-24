using System.Net;

namespace SilverScreen.Core.Account.Session;

public interface ISessionService : ICookieFileProvider
{
    event EventHandler? SessionChanged;

    AccountSession GetCurrentSession();

    ManualSessionCookies? GetManualSessionCookies();

    void SetManualSession(string cookieContent, SessionCookieFormat format);

    void ClearSession();

    CookieFileLease? AcquireCookieFileLease();

    CookieFileLease? ICookieFileProvider.CreateCookieFile() => AcquireCookieFileLease();

    CookieContainer? CreateCookieContainer();
}

public sealed record ManualSessionCookies(SessionCookieFormat Format, string Content);