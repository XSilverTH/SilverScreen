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

namespace SilverScreen.Core.Preferences;

public interface IPreferencesService
{
    AppPreferences GetPreferences();

    /// <summary>
    ///     Persists the supplied preferences and notifies subscribers after the write succeeds.
    /// </summary>
    /// <exception cref="PreferencesPersistenceException">The preferences could not be written.</exception>
    void SavePreferences(AppPreferences preferences);

    event EventHandler<AppPreferences>? PreferencesChanged;
}