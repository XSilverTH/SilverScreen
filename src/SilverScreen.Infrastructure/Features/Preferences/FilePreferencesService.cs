using System.Text.Json;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Features.Preferences;

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
            // Return a copy to avoid external modification bypassing SavePreferences/PreferencesChanged
            return Clone(_current);
        }
    }

    public void SavePreferences(AppPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var cloned = Clone(preferences);
        lock (_lock)
        {
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

    private static AppPreferences Clone(AppPreferences source)
    {
        return new AppPreferences
        {
            Theme = source.Theme,
            MpvExecutablePath = source.MpvExecutablePath,
            VideoQuality = source.VideoQuality,
            YtDlpExecutablePath = source.YtDlpExecutablePath,
            MaxResults = source.MaxResults,
            MarkWatchedVideos = source.MarkWatchedVideos,
            DiscordRichPresenceEnabled = source.DiscordRichPresenceEnabled
        };
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