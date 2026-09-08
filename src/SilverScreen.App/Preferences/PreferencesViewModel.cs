using VideoQualityEnum = SilverScreen.Core.Preferences.VideoQuality;
using Serilog;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Preferences;

public sealed record ExecutablePathStatus(bool Found, string Detail);

public sealed record PreferencesEditorState
{
    public string Theme { get; init; } = ThemeMode.System.ToPersistedString();
    public string VideoQuality { get; init; } = VideoQualityEnum.Best.ToPersistedString();
    public string YtDlpExecutablePath { get; init; } = "yt-dlp";
    public string MpvExecutablePath { get; init; } = "mpv";
    public string PlaybackBackend { get; init; } = PlaybackBackendKind.Embedded.ToPersistedString();
    public bool OpenInFullscreen { get; init; } = true;
    public bool AutoAdvanceNextVideo { get; init; } = true;
    public bool MarkWatchedVideos { get; init; }
    public bool YouTubePlaybackTelemetryEnabled { get; init; }
    public bool DiscordRichPresenceEnabled { get; init; }
    public bool RydEnabled { get; init; }
    public bool SponsorBlockAutoSkipEnabled { get; init; }
    public bool SponsorBlockSegmentDisplayEnabled { get; init; } = true;
    public bool ResumePlaybackAutomatically { get; init; }
    public bool ResumePlaybackOnDemand { get; init; }
    public bool ShortcutOsdEnabled { get; init; } = true;
    public PlayerShortcutBindings Shortcuts { get; init; } = new();


    public IReadOnlyList<string> SponsorBlockCategories { get; init; } =
        [.. Core.Player.SponsorBlockCategories.All];

    public string PreferredSubtitleLanguage { get; init; } = string.Empty;
}

public sealed record PreferencesSaveResult(
    bool Succeeded,
    PreferencesEditorState State,
    string? ErrorMessage = null);

public enum PreferencesMutuallyExclusiveOption
{
    MarkWatchedVideos,
    YouTubePlaybackTelemetry,
    ResumePlaybackAutomatically,
    ResumePlaybackOnDemand
}

public sealed class PreferencesViewModel
{
    public const string PersistenceErrorMessage = "Unable to save preferences. Your changes were not applied.";
    private static readonly ILogger Logger = Log.ForContext<PreferencesViewModel>();

    private readonly IPreferencesService _preferencesService;
    private AppPreferences _preferences;

    public PreferencesViewModel(IPreferencesService preferencesService)
    {
        _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        _preferences = _preferencesService.GetPreferences();
        EditorState = FromPreferences(_preferences);
    }

    public PreferencesEditorState EditorState { get; private set; }


    public PreferencesSaveResult Save(PreferencesEditorState editorState,
        PreferencesMutuallyExclusiveOption? changedOption = null)
    {
        ArgumentNullException.ThrowIfNull(editorState);

        var normalizedState = Normalize(editorState, changedOption);
        _preferences = _preferencesService.GetPreferences();
        var preferences = ToPreferences(normalizedState);

        try
        {
            _preferencesService.SavePreferences(preferences);
            _preferences = preferences;
            EditorState = normalizedState;
            return new PreferencesSaveResult(true, EditorState);
        }
        catch (PreferencesPersistenceException exception)
        {
            Logger.Warning(exception, "Failed to persist preferences");
            _preferences = _preferencesService.GetPreferences();
            EditorState = FromPreferences(_preferences);
            return new PreferencesSaveResult(false, EditorState, PersistenceErrorMessage);
        }
    }

    private static PreferencesEditorState Normalize(PreferencesEditorState state,
        PreferencesMutuallyExclusiveOption? changedOption)
    {
        return changedOption switch
        {
            PreferencesMutuallyExclusiveOption.MarkWatchedVideos when state.MarkWatchedVideos =>
                state with { YouTubePlaybackTelemetryEnabled = false },
            PreferencesMutuallyExclusiveOption.YouTubePlaybackTelemetry when state.YouTubePlaybackTelemetryEnabled =>
                state with { MarkWatchedVideos = false },
            PreferencesMutuallyExclusiveOption.ResumePlaybackAutomatically when state.ResumePlaybackAutomatically =>
                state with { ResumePlaybackOnDemand = false },
            PreferencesMutuallyExclusiveOption.ResumePlaybackOnDemand when state.ResumePlaybackOnDemand =>
                state with { ResumePlaybackAutomatically = false },
            _ => state with
            {
                MarkWatchedVideos = state is { MarkWatchedVideos: true, YouTubePlaybackTelemetryEnabled: false },
                ResumePlaybackOnDemand = state is { ResumePlaybackAutomatically: false, ResumePlaybackOnDemand: true }
            }
        };
    }

    private static PreferencesEditorState FromPreferences(AppPreferences preferences)
    {
        return new PreferencesEditorState
        {
            Theme = preferences.ThemeMode.ToPersistedString(),
            VideoQuality = preferences.Quality.ToPersistedString(),
            YtDlpExecutablePath = preferences.YtDlpExecutablePath,
            MpvExecutablePath = preferences.MpvExecutablePath,
            PlaybackBackend = preferences.PlaybackBackendKind.ToPersistedString(),
            OpenInFullscreen = preferences.OpenInFullscreen,
            AutoAdvanceNextVideo = preferences.AutoAdvanceNextVideo,
            MarkWatchedVideos = preferences.MarkWatchedVideos,
            YouTubePlaybackTelemetryEnabled = preferences.YouTubePlaybackTelemetryEnabled,
            DiscordRichPresenceEnabled = preferences.DiscordRichPresenceEnabled,
            RydEnabled = preferences.RydEnabled,
            SponsorBlockAutoSkipEnabled = preferences.SponsorBlockAutoSkipEnabled,
            SponsorBlockSegmentDisplayEnabled = preferences.SponsorBlockSegmentDisplayEnabled,
            ResumePlaybackAutomatically = preferences.ResumePlaybackAutomatically,
            ResumePlaybackOnDemand = preferences.ResumePlaybackOnDemand,
            ShortcutOsdEnabled = preferences.ShortcutOsdEnabled,
            Shortcuts = preferences.Shortcuts,

            SponsorBlockCategories = [.. preferences.SponsorBlockCategories],
            PreferredSubtitleLanguage = preferences.PreferredSubtitleLanguage
        };
    }

    private static AppPreferences ToPreferences(PreferencesEditorState state)
    {
        return new AppPreferences
        {
            ThemeMode = ThemeModes.Parse(state.Theme),
            Quality = VideoQualities.Parse(state.VideoQuality),
            YtDlpExecutablePath = state.YtDlpExecutablePath,
            MpvExecutablePath = state.MpvExecutablePath,
            PlaybackBackendKind = PlaybackBackends.Parse(state.PlaybackBackend),
            OpenInFullscreen = state.OpenInFullscreen,
            AutoAdvanceNextVideo = state.AutoAdvanceNextVideo,
            MarkWatchedVideos = state.MarkWatchedVideos,
            YouTubePlaybackTelemetryEnabled = state.YouTubePlaybackTelemetryEnabled,
            DiscordRichPresenceEnabled = state.DiscordRichPresenceEnabled,
            RydEnabled = state.RydEnabled,
            SponsorBlockAutoSkipEnabled = state.SponsorBlockAutoSkipEnabled,
            SponsorBlockSegmentDisplayEnabled = state.SponsorBlockSegmentDisplayEnabled,
            ResumePlaybackAutomatically = state.ResumePlaybackAutomatically,
            ResumePlaybackOnDemand = state.ResumePlaybackOnDemand,
            ShortcutOsdEnabled = state.ShortcutOsdEnabled,
            Shortcuts = state.Shortcuts,

            SponsorBlockCategories = [.. state.SponsorBlockCategories],
            PreferredSubtitleLanguage = state.PreferredSubtitleLanguage
        };
    }

    /// <summary>
    ///     Validates an executable path: non-empty, resolvable (direct path or PATH lookup),
    ///     and executable where the platform tracks an executable bit.
    /// </summary>
    public static ExecutablePathStatus ValidateExecutablePath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return new ExecutablePathStatus(false, "Enter an executable path.");

        var path = rawPath.Trim();
        string? candidate;
        if (path.Contains(Path.DirectorySeparatorChar) ||
            path.Contains(Path.AltDirectorySeparatorChar))
        {
            candidate = path;
            if (!File.Exists(candidate))
                return new ExecutablePathStatus(false, $"Not found at {candidate}.");
        }
        else
        {
            candidate = FindOnPath(path);
            if (candidate is null)
                return new ExecutablePathStatus(false, $"“{path}” was not found on PATH.");
        }

        if ((OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) && !IsExecutable(candidate))
            return new ExecutablePathStatus(false, $"{candidate} is not executable.");

        return new ExecutablePathStatus(true, $"Found at {candidate}.");
    }

    private static string? FindOnPath(string fileName)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable))
            return null;

        foreach (var directory in pathVariable.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
                continue;

            try
            {
                var candidate = Path.Combine(directory.Trim(), fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                // Malformed PATH entry — skip it.
            }
        }

        return null;
    }

    private static bool IsExecutable(string path)
    {
        try
        {
            var mode = File.GetUnixFileMode(path);
            return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}