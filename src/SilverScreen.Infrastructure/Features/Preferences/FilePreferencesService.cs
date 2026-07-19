using Serilog;
using System.Text.Json;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Features.Preferences;

public sealed class FilePreferencesService : IPreferencesService
{
    private static readonly ILogger Logger = Log.ForContext<FilePreferencesService>();
    private readonly string _filePath;
    private readonly object _lock = new();
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

        AppPreferences cloned;
        lock (_lock)
        {
            _current = Clone(preferences);
            cloned = Clone(_current);

            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                var json = JsonSerializer.Serialize(cloned, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to save preferences to {PreferencesFilePath}", _filePath);
            }
        }

        PreferencesChanged?.Invoke(this, cloned);
    }

    private AppPreferences LoadOrCreate()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var preferences = JsonSerializer.Deserialize<AppPreferences>(json);
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