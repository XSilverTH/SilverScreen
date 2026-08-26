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

    bool IsValidating => false;

    Task<string> ValidateSessionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(string.Empty);
    }

    void CancelValidation()
    {
    }
}

public sealed record ManualSessionCookies(SessionCookieFormat Format, string Content);