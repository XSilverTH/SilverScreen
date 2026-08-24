using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Search;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Queue;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Infrastructure.Account.Session;

public sealed class InMemorySessionService : ISessionService
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