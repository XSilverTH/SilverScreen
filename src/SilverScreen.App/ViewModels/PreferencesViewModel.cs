using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.ViewModels;

public sealed record PreferencesEditorState
{
    public string Theme { get; init; } = "System";
    public string VideoQuality { get; init; } = "Best";
    public string YtDlpExecutablePath { get; init; } = "yt-dlp";
    public string MpvExecutablePath { get; init; } = "mpv";
    public string PlaybackBackend { get; init; } = PlaybackBackends.ExternalMpv;
    public bool OpenInFullscreen { get; init; } = true;
    public bool AutoAdvanceNextVideo { get; init; } = true;
    public string MaxResultsText { get; init; } = "20";
    public bool MarkWatchedVideos { get; init; }
    public bool YouTubePlaybackTelemetryEnabled { get; init; }
    public bool DiscordRichPresenceEnabled { get; init; }
    public bool SponsorBlockAutoSkipEnabled { get; init; }
    public bool SponsorBlockSegmentDisplayEnabled { get; init; } = true;

    public IReadOnlyList<string> SponsorBlockCategories { get; init; } =
        [.. Core.Models.SponsorBlockCategories.All];

    public string PreferredSubtitleLanguage { get; init; } = string.Empty;
}

public sealed record PreferencesSaveResult(
    bool Succeeded,
    PreferencesEditorState State,
    string? ErrorMessage = null);

public enum PreferencesMutuallyExclusiveOption
{
    MarkWatchedVideos,
    YouTubePlaybackTelemetry
}

public sealed class PreferencesViewModel
{
    private static readonly ILogger Logger = Log.ForContext<PreferencesViewModel>();
    public const string PersistenceErrorMessage = "Unable to save preferences. Your changes were not applied.";

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
        var preferences = ToPreferences(normalizedState, _preferences);

        try
        {
            _preferencesService.SavePreferences(preferences);
            _preferences = preferences;
            EditorState = normalizedState;
            return new PreferencesSaveResult(true, EditorState);
        }
        catch (PreferencesPersistenceException)
        {
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
            _ => state with
            {
                MarkWatchedVideos = state is { MarkWatchedVideos: true, YouTubePlaybackTelemetryEnabled: false }
            }
        };
    }

    private static PreferencesEditorState FromPreferences(AppPreferences preferences)
    {
        return new PreferencesEditorState
        {
            Theme = preferences.Theme,
            VideoQuality = preferences.VideoQuality,
            YtDlpExecutablePath = preferences.YtDlpExecutablePath,
            MpvExecutablePath = preferences.MpvExecutablePath,
            PlaybackBackend = preferences.PlaybackBackend,
            OpenInFullscreen = preferences.OpenInFullscreen,
            AutoAdvanceNextVideo = preferences.AutoAdvanceNextVideo,
            MaxResultsText = preferences.MaxResults.ToString(),
            MarkWatchedVideos = preferences is { MarkWatchedVideos: true, YouTubePlaybackTelemetryEnabled: false },
            YouTubePlaybackTelemetryEnabled = preferences.YouTubePlaybackTelemetryEnabled,
            DiscordRichPresenceEnabled = preferences.DiscordRichPresenceEnabled,
            SponsorBlockAutoSkipEnabled = preferences.SponsorBlockAutoSkipEnabled,
            SponsorBlockSegmentDisplayEnabled = preferences.SponsorBlockSegmentDisplayEnabled,
            SponsorBlockCategories = [.. preferences.SponsorBlockCategories],
            PreferredSubtitleLanguage = preferences.PreferredSubtitleLanguage
        };
    }

    private static AppPreferences ToPreferences(PreferencesEditorState state, AppPreferences current)
    {
        var maxResults = int.TryParse(state.MaxResultsText, out var parsedMaxResults) ? parsedMaxResults : 20;

        return new AppPreferences
        {
            Theme = state.Theme,
            VideoQuality = state.VideoQuality,
            YtDlpExecutablePath = state.YtDlpExecutablePath,
            MpvExecutablePath = state.MpvExecutablePath,
            PlaybackBackend = state.PlaybackBackend,
            OpenInFullscreen = state.OpenInFullscreen,
            AutoAdvanceNextVideo = state.AutoAdvanceNextVideo,
            MaxResults = maxResults,
            MarkWatchedVideos = state is { MarkWatchedVideos: true, YouTubePlaybackTelemetryEnabled: false },
            YouTubePlaybackTelemetryEnabled = state.YouTubePlaybackTelemetryEnabled,
            DiscordRichPresenceEnabled = state.DiscordRichPresenceEnabled,
            SponsorBlockAutoSkipEnabled = state.SponsorBlockAutoSkipEnabled,
            SponsorBlockSegmentDisplayEnabled = state.SponsorBlockSegmentDisplayEnabled,
            SponsorBlockCategories = [.. state.SponsorBlockCategories],
            PreferredSubtitleLanguage = current.PreferredSubtitleLanguage
        };
    }
}