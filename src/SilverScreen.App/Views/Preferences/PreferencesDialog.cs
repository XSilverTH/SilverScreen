using Adw;
using Gtk;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.ViewModels;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Views.Preferences;

public partial class PreferencesDialog : ViewBase<Adw.PreferencesDialog>
{
    private static readonly ILogger Logger = Log.ForContext<PreferencesDialog>();
    private readonly SwitchRow _autoAdvanceNextVideoRow;
    private readonly SwitchRow _discordRichPresenceRow;
    private readonly SwitchRow _fullscreenRow;
    private readonly SwitchRow _markWatchedRow;
    private readonly EntryRow _maxResultsRow;
    private readonly EntryRow _mpvPathRow;
    private readonly StringList _playbackBackendModel;
    private readonly ComboRow _playbackBackendRow;
    private readonly StringList _qualityModel;
    private readonly ComboRow _qualityRow;
    private readonly Action<string> _reportStatus;
    private readonly SwitchRow _sponsorBlockAutoSkipRow;
    private readonly IReadOnlyDictionary<string, SwitchRow> _sponsorBlockCategoryRows;
    private readonly SwitchRow _sponsorBlockDisplayRow;
    private readonly StringList _themeModel;
    private readonly ComboRow _themeRow;
    private readonly PreferencesViewModel _viewModel;
    private readonly SwitchRow _youTubePlaybackTelemetryRow;
    private readonly EntryRow _ytdlpPathRow;

    private bool _loading;

    public PreferencesDialog(IPreferencesService preferencesService, Action<string> reportStatus)
    {
        Logger.Information("Opening PreferencesDialog");
        _viewModel = new PreferencesViewModel(preferencesService);
        _reportStatus = reportStatus;
        _themeRow = GetRequiredObject<ComboRow>("theme_row");
        _ytdlpPathRow = GetRequiredObject<EntryRow>("ytdlp_path_row");
        _maxResultsRow = GetRequiredObject<EntryRow>("max_results_row");
        _mpvPathRow = GetRequiredObject<EntryRow>("mpv_path_row");
        _qualityRow = GetRequiredObject<ComboRow>("quality_row");
        _playbackBackendRow = GetRequiredObject<ComboRow>("playback_backend_row");
        _autoAdvanceNextVideoRow = GetRequiredObject<SwitchRow>("auto_advance_next_video_row");
        _fullscreenRow = GetRequiredObject<SwitchRow>("fullscreen_row");
        _markWatchedRow = GetRequiredObject<SwitchRow>("mark_watched_row");
        _youTubePlaybackTelemetryRow = GetRequiredObject<SwitchRow>("youtube_playback_telemetry_row");
        _discordRichPresenceRow = GetRequiredObject<SwitchRow>("discord_rich_presence_row");
        _themeModel = GetRequiredObject<StringList>("theme_model");
        _qualityModel = GetRequiredObject<StringList>("quality_model");
        _playbackBackendModel = GetRequiredObject<StringList>("playback_backend_model");
        _sponsorBlockAutoSkipRow = GetRequiredObject<SwitchRow>("sponsorblock_auto_skip_row");
        _sponsorBlockDisplayRow = GetRequiredObject<SwitchRow>("sponsorblock_display_row");
        _sponsorBlockCategoryRows = new Dictionary<string, SwitchRow>
        {
            [SponsorBlockCategories.Sponsor] = GetRequiredObject<SwitchRow>("sponsorblock_sponsor_row"),
            [SponsorBlockCategories.SelfPromotion] = GetRequiredObject<SwitchRow>("sponsorblock_selfpromo_row"),
            [SponsorBlockCategories.InteractionReminder] =
                GetRequiredObject<SwitchRow>("sponsorblock_interaction_row"),
            [SponsorBlockCategories.Intro] = GetRequiredObject<SwitchRow>("sponsorblock_intro_row"),
            [SponsorBlockCategories.Outro] = GetRequiredObject<SwitchRow>("sponsorblock_outro_row"),
            [SponsorBlockCategories.Preview] = GetRequiredObject<SwitchRow>("sponsorblock_preview_row"),
            [SponsorBlockCategories.Hook] = GetRequiredObject<SwitchRow>("sponsorblock_hook_row"),
            [SponsorBlockCategories.Filler] = GetRequiredObject<SwitchRow>("sponsorblock_filler_row")
        };

        InitializeFields();
    }

    private void InitializeFields()
    {
        ApplyEditorState(_viewModel.EditorState);
    }

    private void ApplyEditorState(PreferencesEditorState state)
    {
        _loading = true;
        try
        {
            _themeRow.Selected = (uint)GetSelectionIndex(_themeModel, state.Theme);
            _qualityRow.Selected = (uint)GetSelectionIndex(_qualityModel, state.VideoQuality);
            _playbackBackendRow.Selected =
                (uint)GetSelectionIndex(_playbackBackendModel, state.PlaybackBackend);
            _fullscreenRow.Active = state.OpenInFullscreen;
            _autoAdvanceNextVideoRow.Active = state.AutoAdvanceNextVideo;
            ((Editable)_ytdlpPathRow).SetText(state.YtDlpExecutablePath);
            ((Editable)_maxResultsRow).SetText(state.MaxResultsText);
            ((Editable)_mpvPathRow).SetText(state.MpvExecutablePath);
            _markWatchedRow.Active = state.MarkWatchedVideos;
            _youTubePlaybackTelemetryRow.Active = state.YouTubePlaybackTelemetryEnabled;
            _discordRichPresenceRow.Active = state.DiscordRichPresenceEnabled;
            _sponsorBlockAutoSkipRow.Active = state.SponsorBlockAutoSkipEnabled;
            _sponsorBlockDisplayRow.Active = state.SponsorBlockSegmentDisplayEnabled;
            foreach (var (category, row) in _sponsorBlockCategoryRows)
                row.Active = state.SponsorBlockCategories.Contains(category, StringComparer.Ordinal);
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

        var changedOption = ReferenceEquals(sender, _markWatchedRow)
            ? PreferencesMutuallyExclusiveOption.MarkWatchedVideos
            : ReferenceEquals(sender, _youTubePlaybackTelemetryRow)
                ? PreferencesMutuallyExclusiveOption.YouTubePlaybackTelemetry
                : (PreferencesMutuallyExclusiveOption?)null;
        Save(changedOption);
    }

    private void OnRowChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        Save();
    }

    private void Save(PreferencesMutuallyExclusiveOption? changedOption = null)
    {
        var result = _viewModel.Save(CreateEditorState(), changedOption);
        ApplyEditorState(result.State);
        if (!result.Succeeded)
            _reportStatus(result.ErrorMessage ?? PreferencesViewModel.PersistenceErrorMessage);
    }

    private PreferencesEditorState CreateEditorState()
    {
        return _viewModel.EditorState with
        {
            Theme = GetSelectedValue(_themeModel, _themeRow.Selected, "System"),
            VideoQuality = GetSelectedValue(_qualityModel, _qualityRow.Selected, "Best"),
            YtDlpExecutablePath = ((Editable)_ytdlpPathRow).GetText(),
            MpvExecutablePath = ((Editable)_mpvPathRow).GetText(),
            PlaybackBackend = GetSelectedValue(_playbackBackendModel, _playbackBackendRow.Selected,
                PlaybackBackends.ExternalMpv),
            OpenInFullscreen = _fullscreenRow.Active,
            AutoAdvanceNextVideo = _autoAdvanceNextVideoRow.Active,
            MaxResultsText = ((Editable)_maxResultsRow).GetText(),
            MarkWatchedVideos = _markWatchedRow.Active,
            YouTubePlaybackTelemetryEnabled = _youTubePlaybackTelemetryRow.Active,
            DiscordRichPresenceEnabled = _discordRichPresenceRow.Active,
            SponsorBlockAutoSkipEnabled = _sponsorBlockAutoSkipRow.Active,
            SponsorBlockSegmentDisplayEnabled = _sponsorBlockDisplayRow.Active,
            SponsorBlockCategories =
            [
                .. _sponsorBlockCategoryRows
                    .Where(pair => pair.Value.Active)
                    .Select(pair => pair.Key)
            ]
        };
    }
}