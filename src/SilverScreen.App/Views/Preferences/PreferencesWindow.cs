using System;
using Adw;
using Gtk;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Views.Preferences;

public partial class PreferencesWindow : WindowBase<Adw.PreferencesWindow>
{
    private readonly IPreferencesService _preferencesService;
    private readonly ComboRow _themeRow;
    private readonly EntryRow _ytdlpPathRow;
    private readonly EntryRow _maxResultsRow;
    private readonly EntryRow _mpvPathRow;
    private readonly ComboRow _qualityRow;

    private static readonly string[] Themes = { "System", "Light", "Dark" };
    private static readonly string[] Qualities = { "Best", "1080p", "720p", "480p", "360p" };

    private bool _loading;

    public PreferencesWindow(IPreferencesService preferencesService)
    {
        _preferencesService = preferencesService;

        _themeRow = GetRequiredObject<ComboRow>("theme_row");
        _ytdlpPathRow = GetRequiredObject<EntryRow>("ytdlp_path_row");
        _maxResultsRow = GetRequiredObject<EntryRow>("max_results_row");
        _mpvPathRow = GetRequiredObject<EntryRow>("mpv_path_row");
        _qualityRow = GetRequiredObject<ComboRow>("quality_row");

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
            var themeList = Gtk.StringList.New(Themes);
            _themeRow.Model = themeList;
            _themeRow.Selected = (uint)Array.IndexOf(Themes, prefs.Theme);

            // Populate quality dropdown
            var qualityList = Gtk.StringList.New(Qualities);
            _qualityRow.Model = qualityList;
            _qualityRow.Selected = (uint)Array.IndexOf(Qualities, prefs.VideoQuality);

            // Set entry values
            ((Gtk.Editable)_ytdlpPathRow).SetText(prefs.YtDlpExecutablePath);
            ((Gtk.Editable)_maxResultsRow).SetText(prefs.MaxResults.ToString());
            ((Gtk.Editable)_mpvPathRow).SetText(prefs.MpvExecutablePath);
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

        // Handle text entry changes
        ((Gtk.Editable)_ytdlpPathRow).OnChanged += OnRowChanged;
        ((Gtk.Editable)_maxResultsRow).OnChanged += OnRowChanged;
        ((Gtk.Editable)_mpvPathRow).OnChanged += OnRowChanged;
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
        var theme = (themeIndex >= 0 && themeIndex < Themes.Length) ? Themes[themeIndex] : "System";

        var qualityIndex = (int)_qualityRow.Selected;
        var quality = (qualityIndex >= 0 && qualityIndex < Qualities.Length) ? Qualities[qualityIndex] : "Best";

        var maxResultsText = ((Gtk.Editable)_maxResultsRow).GetText();
        if (!int.TryParse(maxResultsText, out var maxResults))
        {
            maxResults = 20;
        }

        var prefs = new AppPreferences
        {
            Theme = theme,
            VideoQuality = quality,
            YtDlpExecutablePath = ((Gtk.Editable)_ytdlpPathRow).GetText() ?? "yt-dlp",
            MpvExecutablePath = ((Gtk.Editable)_mpvPathRow).GetText() ?? "mpv",
            MaxResults = maxResults
        };

        _preferencesService.SavePreferences(prefs);
    }
}
