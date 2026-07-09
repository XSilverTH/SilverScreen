using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Mock;

public sealed class MockSessionService : ISessionService
{
    public event EventHandler? SessionChanged;

    public AccountSession GetCurrentSession() => AccountSession.SignedOut;

    public ManualSessionCookies? GetManualSessionCookies() => null;

    public void SetManualSession(string cookieContent, SessionCookieFormat format)
    {
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSession()
    {
        // No-op stub
    }
}
