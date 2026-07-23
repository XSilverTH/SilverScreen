using Adw;
using Gtk;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Views.Preferences;

public partial class PreferencesWindow : WindowBase<Adw.PreferencesWindow>
{
    private readonly SwitchRow _discordRichPresenceRow;
    private readonly SwitchRow _fullscreenRow;
    private readonly SwitchRow _markWatchedRow;
    private readonly EntryRow _maxResultsRow;
    private readonly EntryRow _mpvPathRow;
    private readonly StringList _playbackBackendModel;
    private readonly ComboRow _playbackBackendRow;
    private readonly IPreferencesService _preferencesService;
    private readonly StringList _qualityModel;
    private readonly ComboRow _qualityRow;
    private readonly Action<string> _reportStatus;
    private readonly StringList _themeModel;
    private readonly ComboRow _themeRow;
    private readonly EntryRow _ytdlpPathRow;

    private bool _loading;

    public PreferencesWindow(IPreferencesService preferencesService, Action<string> reportStatus)
    {
        _preferencesService = preferencesService;
        _reportStatus = reportStatus;

        _themeRow = GetRequiredObject<ComboRow>("theme_row");
        _ytdlpPathRow = GetRequiredObject<EntryRow>("ytdlp_path_row");
        _maxResultsRow = GetRequiredObject<EntryRow>("max_results_row");
        _mpvPathRow = GetRequiredObject<EntryRow>("mpv_path_row");
        _qualityRow = GetRequiredObject<ComboRow>("quality_row");
        _playbackBackendRow = GetRequiredObject<ComboRow>("playback_backend_row");
        _fullscreenRow = GetRequiredObject<SwitchRow>("fullscreen_row");
        _markWatchedRow = GetRequiredObject<SwitchRow>("mark_watched_row");
        _discordRichPresenceRow = GetRequiredObject<SwitchRow>("discord_rich_presence_row");
        _themeModel = GetRequiredObject<StringList>("theme_model");
        _qualityModel = GetRequiredObject<StringList>("quality_model");
        _playbackBackendModel = GetRequiredObject<StringList>("playback_backend_model");

        InitializeFields();
    }

    private void InitializeFields()
    {
        _loading = true;
        try
        {
            var prefs = _preferencesService.GetPreferences();

            // Populate theme dropdown
            _themeRow.Selected = (uint)GetSelectionIndex(_themeModel, prefs.Theme);

            // Populate quality dropdown
            _qualityRow.Selected = (uint)GetSelectionIndex(_qualityModel, prefs.VideoQuality);
            _playbackBackendRow.Selected =
                (uint)GetSelectionIndex(_playbackBackendModel, prefs.PlaybackBackend);
            _fullscreenRow.Active = prefs.OpenInFullscreen;

            // Set entry values
            ((Editable)_ytdlpPathRow).SetText(prefs.YtDlpExecutablePath);
            ((Editable)_maxResultsRow).SetText(prefs.MaxResults.ToString());
            ((Editable)_mpvPathRow).SetText(prefs.MpvExecutablePath);
            _markWatchedRow.Active = prefs.MarkWatchedVideos;
            _discordRichPresenceRow.Active = prefs.DiscordRichPresenceEnabled;
        }
        finally
        {
            _loading = false;
        }
    }

    private static int GetSelectionIndex(StringList model, string value)
    {
        for (uint i = 0; i < model.GetNItems(); i++)
            if (model.GetString(i) == value)
                return (int)i;

        return -1;
    }

    private static string GetSelectedValue(StringList model, uint selected, string fallback)
    {
        var selectedIndex = (int)selected;
        var itemCount = (int)model.GetNItems();
        return selectedIndex >= 0 && selectedIndex < itemCount
            ? model.GetString(selected) ?? fallback
            : fallback;
    }


    private void OnRowNotify(object? sender, EventArgs e)
    {
        if (_loading) return;
        Save();
    }

    private void OnRowChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        Save();
    }

    private void Save()
    {
        var theme = GetSelectedValue(_themeModel, _themeRow.Selected, "System");

        var quality = GetSelectedValue(_qualityModel, _qualityRow.Selected, "Best");

        var maxResultsText = ((Editable)_maxResultsRow).GetText();
        if (!int.TryParse(maxResultsText, out var maxResults)) maxResults = 20;

        var prefs = new AppPreferences
        {
            Theme = theme,
            VideoQuality = quality,
            YtDlpExecutablePath = ((Editable)_ytdlpPathRow).GetText(),
            MpvExecutablePath = ((Editable)_mpvPathRow).GetText(),
            PlaybackBackend = GetSelectedValue(_playbackBackendModel, _playbackBackendRow.Selected,
                PlaybackBackends.ExternalMpv),
            OpenInFullscreen = _fullscreenRow.Active,
            MaxResults = maxResults,
            MarkWatchedVideos = _markWatchedRow.Active,
            DiscordRichPresenceEnabled = _discordRichPresenceRow.Active
        };

        try
        {
            _preferencesService.SavePreferences(prefs);
        }
        catch (PreferencesPersistenceException)
        {
            InitializeFields();
            _reportStatus("Unable to save preferences. Your changes were not applied.");
        }
    }
}