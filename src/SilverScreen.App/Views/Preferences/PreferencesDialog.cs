using Adw;
using Gtk;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.ViewModels;
using XSTH.Blueprint.Helpers;
using Functions = Gdk.Functions;

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
    private readonly SwitchRow _resumePlaybackAutomaticallyRow;
    private readonly SwitchRow _resumePlaybackOnDemandRow;
    private readonly IReadOnlyDictionary<string, Button> _shortcutRows;
    private readonly Dictionary<string, string[]> _shortcutValues = new(StringComparer.Ordinal);
    private readonly SwitchRow _sponsorBlockAutoSkipRow;
    private readonly IReadOnlyDictionary<string, SwitchRow> _sponsorBlockCategoryRows;
    private readonly SwitchRow _sponsorBlockDisplayRow;

    private readonly StringList _themeModel;
    private readonly ComboRow _themeRow;
    private readonly PreferencesViewModel _viewModel;
    private readonly SwitchRow _youTubePlaybackTelemetryRow;
    private readonly EntryRow _ytdlpPathRow;
    private string? _capturingShortcut;

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
        _resumePlaybackAutomaticallyRow = GetRequiredObject<SwitchRow>("resume_playback_automatically_row");
        _resumePlaybackOnDemandRow = GetRequiredObject<SwitchRow>("resume_playback_on_demand_row");

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
        _shortcutRows = new Dictionary<string, Button>(StringComparer.Ordinal)
        {
            ["TogglePause"] = GetRequiredObject<Button>("shortcut_toggle_pause_button"),
            ["SeekBackward"] = GetRequiredObject<Button>("shortcut_seek_backward_button"),
            ["SeekForward"] = GetRequiredObject<Button>("shortcut_seek_forward_button"),
            ["StepFrameBackward"] = GetRequiredObject<Button>("shortcut_step_frame_backward_button"),
            ["StepFrameForward"] = GetRequiredObject<Button>("shortcut_step_frame_forward_button"),
            ["ToggleMute"] = GetRequiredObject<Button>("shortcut_toggle_mute_button"),
            ["VolumeUp"] = GetRequiredObject<Button>("shortcut_volume_up_button"),
            ["VolumeDown"] = GetRequiredObject<Button>("shortcut_volume_down_button"),
            ["SeekToBeginning"] = GetRequiredObject<Button>("shortcut_seek_to_beginning_button"),
            ["ReturnToShell"] = GetRequiredObject<Button>("shortcut_return_to_shell_button"),
            ["ToggleVideoInfo"] = GetRequiredObject<Button>("shortcut_toggle_video_info_button"),
            ["SpeedDecrease"] = GetRequiredObject<Button>("shortcut_speed_decrease_button"),
            ["SpeedIncrease"] = GetRequiredObject<Button>("shortcut_speed_increase_button"),
            ["NextVideo"] = GetRequiredObject<Button>("shortcut_next_video_button"),
            ["PreviousVideo"] = GetRequiredObject<Button>("shortcut_previous_video_button"),
            ["ToggleFullscreen"] = GetRequiredObject<Button>("shortcut_toggle_fullscreen_button"),
            ["PreferredSubtitle"] = GetRequiredObject<Button>("shortcut_preferred_subtitle_button"),
            ["ResumeOrSkip"] = GetRequiredObject<Button>("shortcut_resume_or_skip_button")
        };

        foreach (var button in _shortcutRows.Values)
            button.OnClicked += OnShortcutButtonClicked;

        var keyController = EventControllerKey.New();
        keyController.SetPropagationPhase(PropagationPhase.Capture);
        keyController.OnKeyPressed += (_, args) => CaptureShortcut(args.Keyval);
        Widget.AddController(keyController);

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
            _resumePlaybackAutomaticallyRow.Active = state.ResumePlaybackAutomatically;
            _resumePlaybackOnDemandRow.Active = state.ResumePlaybackOnDemand;
            ApplyShortcuts(state.Shortcuts);

            foreach (var (category, row) in _sponsorBlockCategoryRows)
                row.Active = state.SponsorBlockCategories.Contains(category, StringComparer.Ordinal);
        }
        finally
        {
            _loading = false;
        }
    }

    private void ApplyShortcuts(PlayerShortcutBindings shortcuts)
    {
        SetShortcut("TogglePause", shortcuts.TogglePause);
        SetShortcut("SeekBackward", shortcuts.SeekBackward);
        SetShortcut("SeekForward", shortcuts.SeekForward);
        SetShortcut("StepFrameBackward", shortcuts.StepFrameBackward);
        SetShortcut("StepFrameForward", shortcuts.StepFrameForward);
        SetShortcut("ToggleMute", shortcuts.ToggleMute);
        SetShortcut("VolumeUp", shortcuts.VolumeUp);
        SetShortcut("VolumeDown", shortcuts.VolumeDown);
        SetShortcut("SeekToBeginning", shortcuts.SeekToBeginning);
        SetShortcut("ReturnToShell", shortcuts.ReturnToShell);
        SetShortcut("ToggleVideoInfo", shortcuts.ToggleVideoInfo);
        SetShortcut("SpeedDecrease", shortcuts.SpeedDecrease);
        SetShortcut("SpeedIncrease", shortcuts.SpeedIncrease);
        SetShortcut("NextVideo", shortcuts.NextVideo);
        SetShortcut("PreviousVideo", shortcuts.PreviousVideo);
        SetShortcut("ToggleFullscreen", shortcuts.ToggleFullscreen);
        SetShortcut("PreferredSubtitle", shortcuts.PreferredSubtitle);
        SetShortcut("ResumeOrSkip", shortcuts.ResumeOrSkip);
    }

    private void SetShortcut(string name, IEnumerable<string> shortcuts)
    {
        var values = shortcuts.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        _shortcutValues[name] = values;
        _shortcutRows[name].SetLabel(values.Length == 0 ? "Unassigned" : GetShortcutLabel(values[0]));
    }

    private void OnShortcutButtonClicked(object? sender, EventArgs args)
    {
        if (sender is not Button button) return;
        var pair = _shortcutRows.FirstOrDefault(item => ReferenceEquals(item.Value, button));
        if (pair.Key is not { } name) return;
        if (_capturingShortcut is { } previous &&
            _shortcutRows.TryGetValue(previous, out var previousButton))
            previousButton.RemoveCssClass("suggested-action");

        _capturingShortcut = name;
        button.SetLabel("Press a key…");
        button.AddCssClass("suggested-action");
        button.GrabFocus();
    }

    private bool CaptureShortcut(uint keyval)
    {
        if (_capturingShortcut is not { } name) return false;

        var canonical = CanonicalKeyName(keyval);
        if (canonical is null) return true;

        var button = _shortcutRows[name];
        _shortcutValues[name] = [canonical];
        button.RemoveCssClass("suggested-action");
        button.SetLabel(GetShortcutLabel(canonical));
        _capturingShortcut = null;
        Save();
        return true;
    }

    private static string? CanonicalKeyName(uint keyval)
    {
        var normalized = Functions.KeyvalToLower(keyval);
        return normalized == 0 ? null : Functions.KeyvalName(normalized);
    }

    private static string GetShortcutLabel(string keyName)
    {
        return keyName switch
        {
            "space" => "Space",
            "Return" => "Enter",
            "KP_Enter" => "Keypad Enter",
            "Escape" => "Escape",
            "Left" => "Left Arrow",
            "Right" => "Right Arrow",
            "Up" => "Up Arrow",
            "Down" => "Down Arrow",
            "comma" => ",",
            "less" => "<",
            "period" => ".",
            "greater" => ">",
            "bracketleft" => "[",
            "braceleft" => "{",
            "bracketright" => "]",
            "braceright" => "}",
            _ when keyName.Length == 1 && char.IsLetterOrDigit(keyName[0]) => keyName.ToUpperInvariant(),
            _ => keyName.Replace('_', ' ')
        };
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
                : ReferenceEquals(sender, _resumePlaybackAutomaticallyRow)
                    ? PreferencesMutuallyExclusiveOption.ResumePlaybackAutomatically
                    : ReferenceEquals(sender, _resumePlaybackOnDemandRow)
                        ? PreferencesMutuallyExclusiveOption.ResumePlaybackOnDemand
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
            ResumePlaybackAutomatically = _resumePlaybackAutomaticallyRow.Active,
            ResumePlaybackOnDemand = _resumePlaybackOnDemandRow.Active,
            Shortcuts = CreateShortcutBindings(),
            SponsorBlockCategories =
            [
                .. _sponsorBlockCategoryRows
                    .Where(pair => pair.Value.Active)
                    .Select(pair => pair.Key)
            ]
        };
    }

    private PlayerShortcutBindings CreateShortcutBindings()
    {
        return new PlayerShortcutBindings
        {
            TogglePause = ReadShortcut("TogglePause"),
            SeekBackward = ReadShortcut("SeekBackward"),
            SeekForward = ReadShortcut("SeekForward"),
            StepFrameBackward = ReadShortcut("StepFrameBackward"),
            StepFrameForward = ReadShortcut("StepFrameForward"),
            ToggleMute = ReadShortcut("ToggleMute"),
            VolumeUp = ReadShortcut("VolumeUp"),
            VolumeDown = ReadShortcut("VolumeDown"),
            SeekToBeginning = ReadShortcut("SeekToBeginning"),
            ReturnToShell = ReadShortcut("ReturnToShell"),
            ToggleVideoInfo = ReadShortcut("ToggleVideoInfo"),
            SpeedDecrease = ReadShortcut("SpeedDecrease"),
            SpeedIncrease = ReadShortcut("SpeedIncrease"),
            NextVideo = ReadShortcut("NextVideo"),
            PreviousVideo = ReadShortcut("PreviousVideo"),
            ToggleFullscreen = ReadShortcut("ToggleFullscreen"),
            PreferredSubtitle = ReadShortcut("PreferredSubtitle"),
            ResumeOrSkip = ReadShortcut("ResumeOrSkip")
        };
    }

    private string[] ReadShortcut(string name)
    {
        return _shortcutValues.TryGetValue(name, out var values) ? [.. values] : [];
    }
}