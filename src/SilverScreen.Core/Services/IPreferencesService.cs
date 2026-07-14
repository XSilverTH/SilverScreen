using SilverScreen.Core.Models;

namespace SilverScreen.Core.Services;

public interface IPreferencesService
{
    AppPreferences GetPreferences();
    void SavePreferences(AppPreferences preferences);
    event EventHandler<AppPreferences>? PreferencesChanged;
}
