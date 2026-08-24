using System.Text.Json;
using Serilog;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Infrastructure.Preferences;

public sealed class FilePreferencesService : IPreferencesService
{
    private static readonly ILogger Logger = Log.ForContext<FilePreferencesService>();
    private readonly string _filePath;
    private readonly Lock _lock = new();
    private AppPreferences _current;

    public FilePreferencesService() : this(GetDefaultPreferencesFilePath())
    {
    }

    public FilePreferencesService(string filePath)
    {
        _filePath = filePath;
        _current = LoadOrCreate();
    }

    public event EventHandler<AppPreferences>? PreferencesChanged;

    public AppPreferences GetPreferences()
    {
        lock (_lock)
        {
            return Clone(_current);
        }
    }

    public void SavePreferences(AppPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var cloned = Clone(preferences);
        lock (_lock)
        {
            if (cloned == _current)
                return;
            try
            {
                WriteAtomically(cloned);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to save preferences to {PreferencesFilePath}", _filePath);
                throw new PreferencesPersistenceException(_filePath, ex);
            }

            _current = cloned;
        }

        PreferencesChanged?.Invoke(this, Clone(cloned));
    }

    private static AppPreferences Clone(AppPreferences preferences)
    {
        return preferences with { Shortcuts = preferences.Shortcuts with { } };
    }

    private void WriteAtomically(AppPreferences preferences)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory ?? Directory.GetCurrentDirectory(),
            $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, preferences, PreferencesJsonContext.Default.AppPreferences);
                stream.Flush(true);
            }

            File.Move(temporaryPath, _filePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private AppPreferences LoadOrCreate()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var preferences = JsonSerializer.Deserialize(json, PreferencesJsonContext.Default.AppPreferences);
                if (preferences is not null) return preferences;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to load preferences from {PreferencesFilePath}", _filePath);
        }

        return new AppPreferences();
    }

    private static string GetDefaultPreferencesFilePath()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(configHome)) return Path.Combine(configHome, "SilverScreen", "preferences.json");

        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        configHome = string.IsNullOrWhiteSpace(userHome)
            ? Path.GetTempPath()
            : Path.Combine(userHome, ".config");

        return Path.Combine(configHome, "SilverScreen", "preferences.json");
    }
}