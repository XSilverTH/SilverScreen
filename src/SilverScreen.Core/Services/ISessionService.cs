using SilverScreen.Core.Models;

namespace SilverScreen.Core.Services;

public interface ISessionService
{
    event EventHandler? SessionChanged;

    AccountSession GetCurrentSession();

    ManualSessionCookies? GetManualSessionCookies();

    void SetManualSession(string cookieContent, SessionCookieFormat format);

    void ClearSession();
}

public sealed record ManualSessionCookies(SessionCookieFormat Format, string Content);