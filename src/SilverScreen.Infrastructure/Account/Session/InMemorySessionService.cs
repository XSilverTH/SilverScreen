using System.Net;
using SilverScreen.Core.Account.Session;

namespace SilverScreen.Infrastructure.Account.Session;

public sealed class InMemorySessionService(string? tempRoot = null) : ISessionService
{
    private readonly Lock _gate = new();
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
                    "YouTube session",
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

    public CookieFileLease? AcquireCookieFileLease()
    {
        lock (_gate)
        {
            if (_manualCookies is null || _manualCookies.Format != SessionCookieFormat.NetscapeCookiesText ||
                string.IsNullOrWhiteSpace(_manualCookies.Content))
                return null;

            return TemporaryCookieFile.CreateLease(_manualCookies.Content, tempRoot);
        }
    }

    public CookieFileLease? CreateCookieFile()
    {
        return AcquireCookieFileLease();
    }

    public CookieContainer? CreateCookieContainer()
    {
        lock (_gate)
        {
            if (_manualCookies is null || _manualCookies.Format != SessionCookieFormat.NetscapeCookiesText ||
                string.IsNullOrWhiteSpace(_manualCookies.Content))
                return null;

            return NetscapeCookieParser.CreateCookieContainer(_manualCookies.Content);
        }
    }

    public void SetManualSession(string cookieContent, SessionCookieFormat format)
    {
        if (string.IsNullOrWhiteSpace(cookieContent))
            throw new ArgumentException("Manual session cookie content cannot be empty.", nameof(cookieContent));

        lock (_gate)
        {
            _manualCookies = new ManualSessionCookies(format, cookieContent);
        }

        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSession()
    {
        bool changed;
        lock (_gate)
        {
            changed = _manualCookies is not null;
            _manualCookies = null;
        }

        if (changed)
            SessionChanged?.Invoke(this, EventArgs.Empty);
    }
}