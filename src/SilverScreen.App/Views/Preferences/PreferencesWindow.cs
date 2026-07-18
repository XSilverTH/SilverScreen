using Adw;
using Gtk;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Views.Preferences;

public class PreferencesWindow : WindowBase<Adw.PreferencesWindow>
{
    private static readonly string[] Themes = ["System", "Light", "Dark"];
    private static readonly string[] Qualities = ["Best", "1080p", "720p", "480p", "360p"];
    private readonly SwitchRow _markWatchedRow;
    private readonly EntryRow _maxResultsRow;
    private readonly EntryRow _mpvPathRow;
    private readonly IPreferencesService _preferencesService;
    private readonly ComboRow _qualityRow;
    private readonly ComboRow _themeRow;
    private readonly EntryRow _ytdlpPathRow;

    private bool _loading;

    public PreferencesWindow(IPreferencesService preferencesService)
    {
        _preferencesService = preferencesService;

        _themeRow = GetRequiredObject<ComboRow>("theme_row");
        _ytdlpPathRow = GetRequiredObject<EntryRow>("ytdlp_path_row");
        _maxResultsRow = GetRequiredObject<EntryRow>("max_results_row");
        _mpvPathRow = GetRequiredObject<EntryRow>("mpv_path_row");
        _qualityRow = GetRequiredObject<ComboRow>("quality_row");
        _markWatchedRow = GetRequiredObject<SwitchRow>("mark_watched_row");

        InitializeFields();
        SetupEventHandlers();
    }

    private void InitializeFields()
    {
        _loading = true;
        try
        {
            var prefs = _preferencesService.GetPreferences();

            // Populate theme dropdown
            var themeList = StringList.New(Themes);
            _themeRow.Model = themeList;
            _themeRow.Selected = (uint)Array.IndexOf(Themes, prefs.Theme);

            // Populate quality dropdown
            var qualityList = StringList.New(Qualities);
            _qualityRow.Model = qualityList;
            _qualityRow.Selected = (uint)Array.IndexOf(Qualities, prefs.VideoQuality);

            // Set entry values
            ((Editable)_ytdlpPathRow).SetText(prefs.YtDlpExecutablePath);
            ((Editable)_maxResultsRow).SetText(prefs.MaxResults.ToString());
            ((Editable)_mpvPathRow).SetText(prefs.MpvExecutablePath);
            _markWatchedRow.Active = prefs.MarkWatchedVideos;
        }
        finally
        {
            _loading = false;
        }
    }

    private void SetupEventHandlers()
    {
        // Handle dropdown selection changes
        _themeRow.OnNotify += OnRowNotify;
        _qualityRow.OnNotify += OnRowNotify;
        _markWatchedRow.OnNotify += OnRowNotify;

        // Handle text entry changes
        ((Editable)_ytdlpPathRow).OnChanged += OnRowChanged;
        ((Editable)_maxResultsRow).OnChanged += OnRowChanged;
        ((Editable)_mpvPathRow).OnChanged += OnRowChanged;
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
        var themeIndex = (int)_themeRow.Selected;
        var theme = themeIndex >= 0 && themeIndex < Themes.Length ? Themes[themeIndex] : "System";

        var qualityIndex = (int)_qualityRow.Selected;
        var quality = qualityIndex >= 0 && qualityIndex < Qualities.Length ? Qualities[qualityIndex] : "Best";

        var maxResultsText = ((Editable)_maxResultsRow).GetText();
        if (!int.TryParse(maxResultsText, out var maxResults)) maxResults = 20;

        var prefs = new AppPreferences
        {
            Theme = theme,
            VideoQuality = quality,
            YtDlpExecutablePath = ((Editable)_ytdlpPathRow).GetText(),
            MpvExecutablePath = ((Editable)_mpvPathRow).GetText(),
            MaxResults = maxResults,
            MarkWatchedVideos = _markWatchedRow.Active
        };

        _preferencesService.SavePreferences(prefs);
    }
}