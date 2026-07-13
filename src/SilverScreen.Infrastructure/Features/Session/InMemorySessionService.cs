using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Features.Session;

public sealed class InMemorySessionService : ISessionService
{
    private readonly object _gate = new();
    private ManualSessionCookies? _manualCookies;

    public event EventHandler? SessionChanged;

    public AccountSession GetCurrentSession()
    {
        lock (_gate)
        {
            return _manualCookies is null
                ? AccountSession.SignedOut
                : new AccountSession(
                    true,
                    "Manual YouTube session",
                    HasManualSession: true,
                    CookieFormat: _manualCookies.Format);
        }
    }

    public ManualSessionCookies? GetManualSessionCookies()
    {
        lock (_gate)
        {
            return _manualCookies;
        }
    }

    public void SetManualSession(string cookieContent, SessionCookieFormat format)
    {
        if (string.IsNullOrWhiteSpace(cookieContent))
        {
            throw new ArgumentException("Manual session cookie content cannot be empty.", nameof(cookieContent));
        }

        lock (_gate)
        {
            _manualCookies = new ManualSessionCookies(format, cookieContent);
        }

        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSession()
    {
        var changed = false;
        lock (_gate)
        {
            changed = _manualCookies is not null;
            _manualCookies = null;
        }

        if (changed)
        {
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}