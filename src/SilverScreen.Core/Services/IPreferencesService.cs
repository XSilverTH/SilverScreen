using SilverScreen.Core.Models;

namespace SilverScreen.Core.Services;

public interface IPreferencesService
{
    AppPreferences GetPreferences();
    /// <summary>
    /// Persists the supplied preferences and notifies subscribers after the write succeeds.
    /// </summary>
    /// <exception cref="PreferencesPersistenceException">The preferences could not be written.</exception>
    void SavePreferences(AppPreferences preferences);
    event EventHandler<AppPreferences>? PreferencesChanged;
}