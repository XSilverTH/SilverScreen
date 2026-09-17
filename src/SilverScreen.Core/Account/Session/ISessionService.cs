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

    Task SetManualSessionAsync(string cookieContent, SessionCookieFormat format)
    {
        return Task.Run(() => SetManualSession(cookieContent, format));
    }

    void ClearSession();

    Task ClearSessionAsync()
    {
        return Task.Run(ClearSession);
    }

    CookieFileLease? AcquireCookieFileLease();

    CookieContainer? CreateCookieContainer();

    Task<string> ValidateSessionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(string.Empty);
    }

    void CancelValidation()
    {
    }
}

public sealed record ManualSessionCookies(SessionCookieFormat Format, string Content);