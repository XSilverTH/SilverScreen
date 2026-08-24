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

namespace SilverScreen.Core.Account.Session;

public interface ISessionService
{
    event EventHandler? SessionChanged;

    AccountSession GetCurrentSession();

    ManualSessionCookies? GetManualSessionCookies();

    void SetManualSession(string cookieContent, SessionCookieFormat format);

    void ClearSession();
}

public sealed record ManualSessionCookies(SessionCookieFormat Format, string Content);