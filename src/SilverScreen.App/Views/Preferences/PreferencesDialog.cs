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
    [BlueprintWidget("theme_row")]
    private ComboRow _themeRow = null!;

    [BlueprintWidget("ytdlp_path_row")]
    private EntryRow _ytdlpPathRow = null!;

    [BlueprintWidget("max_results_row")]
    private EntryRow _maxResultsRow = null!;

    [BlueprintWidget("mpv_path_row")]
    private EntryRow _mpvPathRow = null!;

    [BlueprintWidget("quality_row")]
    private ComboRow _qualityRow = null!;

    [BlueprintWidget("playback_backend_row")]
    private ComboRow _playbackBackendRow = null!;

    [BlueprintWidget("auto_advance_next_video_row")]
    private SwitchRow _autoAdvanceNextVideoRow = null!;

    [BlueprintWidget("fullscreen_row")]
    private SwitchRow _fullscreenRow = null!;

    [BlueprintWidget("mark_watched_row")]
    private SwitchRow _markWatchedRow = null!;

    [BlueprintWidget("youtube_playback_telemetry_row")]
    private SwitchRow _youTubePlaybackTelemetryRow = null!;

    [BlueprintWidget("discord_rich_presence_row")]
    private SwitchRow _discordRichPresenceRow = null!;

    [BlueprintWidget("theme_model")]
    private StringList _themeModel = null!;

    [BlueprintWidget("quality_model")]
    private StringList _qualityModel = null!;

    [BlueprintWidget("playback_backend_model")]
    private StringList _playbackBackendModel = null!;

    [BlueprintWidget("sponsorblock_auto_skip_row")]
    private SwitchRow _sponsorBlockAutoSkipRow = null!;

    [BlueprintWidget("sponsorblock_display_row")]
    private SwitchRow _sponsorBlockDisplayRow = null!;

    [BlueprintWidget("resume_playback_automatically_row")]
    private SwitchRow _resumePlaybackAutomaticallyRow = null!;

    [BlueprintWidget("resume_playback_on_demand_row")]
    private SwitchRow _resumePlaybackOnDemandRow = null!;

    [BlueprintWidget("sponsorblock_sponsor_row")]
    private SwitchRow _sponsorBlockSponsorRow = null!;

    [BlueprintWidget("sponsorblock_selfpromo_row")]
    private SwitchRow _sponsorBlockSelfPromoRow = null!;

    [BlueprintWidget("sponsorblock_interaction_row")]
    private SwitchRow _sponsorBlockInteractionRow = null!;

    [BlueprintWidget("sponsorblock_intro_row")]
    private SwitchRow _sponsorBlockIntroRow = null!;

    [BlueprintWidget("sponsorblock_outro_row")]
    private SwitchRow _sponsorBlockOutroRow = null!;

    [BlueprintWidget("sponsorblock_preview_row")]
    private SwitchRow _sponsorBlockPreviewRow = null!;

    [BlueprintWidget("sponsorblock_hook_row")]
    private SwitchRow _sponsorBlockHookRow = null!;

    [BlueprintWidget("sponsorblock_filler_row")]
    private SwitchRow _sponsorBlockFillerRow = null!;

    [BlueprintWidget("shortcut_toggle_pause_button")]
    private Button _shortcutTogglePauseButton = null!;

    [BlueprintWidget("shortcut_seek_backward_button")]
    private Button _shortcutSeekBackwardButton = null!;

    [BlueprintWidget("shortcut_seek_forward_button")]
    private Button _shortcutSeekForwardButton = null!;

    [BlueprintWidget("shortcut_step_frame_backward_button")]
    private Button _shortcutStepFrameBackwardButton = null!;

    [BlueprintWidget("shortcut_step_frame_forward_button")]
    private Button _shortcutStepFrameForwardButton = null!;

    [BlueprintWidget("shortcut_toggle_mute_button")]
    private Button _shortcutToggleMuteButton = null!;

    [BlueprintWidget("shortcut_volume_up_button")]
    private Button _shortcutVolumeUpButton = null!;

    [BlueprintWidget("shortcut_volume_down_button")]
    private Button _shortcutVolumeDownButton = null!;

    [BlueprintWidget("shortcut_seek_to_beginning_button")]
    private Button _shortcutSeekToBeginningButton = null!;

    [BlueprintWidget("shortcut_return_to_shell_button")]
    private Button _shortcutReturnToShellButton = null!;

    [BlueprintWidget("shortcut_toggle_video_info_button")]
    private Button _shortcutToggleVideoInfoButton = null!;

    [BlueprintWidget("shortcut_speed_decrease_button")]
    private Button _shortcutSpeedDecreaseButton = null!;

    [BlueprintWidget("shortcut_speed_increase_button")]
    private Button _shortcutSpeedIncreaseButton = null!;

    [BlueprintWidget("shortcut_next_video_button")]
    private Button _shortcutNextVideoButton = null!;

    [BlueprintWidget("shortcut_previous_video_button")]
    private Button _shortcutPreviousVideoButton = null!;

    [BlueprintWidget("shortcut_toggle_fullscreen_button")]
    private Button _shortcutToggleFullscreenButton = null!;

    [BlueprintWidget("shortcut_preferred_subtitle_button")]
    private Button _shortcutPreferredSubtitleButton = null!;

    [BlueprintWidget("shortcut_resume_or_skip_button")]
    private Button _shortcutResumeOrSkipButton = null!;

    private readonly Action<string> _reportStatus;
    private readonly IReadOnlyDictionary<string, Button> _shortcutRows;
    private readonly Dictionary<string, string[]> _shortcutValues = new(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, SwitchRow> _sponsorBlockCategoryRows;
    private readonly PreferencesViewModel _viewModel;
    private string? _capturingShortcut;

    private bool _loading;

    public PreferencesDialog(IPreferencesService preferencesService, Action<string> reportStatus)
    {
        Logger.Information("Opening PreferencesDialog");
        _viewModel = new PreferencesViewModel(preferencesService);
        _reportStatus = reportStatus;
        _sponsorBlockCategoryRows = new Dictionary<string, SwitchRow>
        {
            [SponsorBlockCategories.Sponsor] = _sponsorBlockSponsorRow,
            [SponsorBlockCategories.SelfPromotion] = _sponsorBlockSelfPromoRow,
            [SponsorBlockCategories.InteractionReminder] = _sponsorBlockInteractionRow,
            [SponsorBlockCategories.Intro] = _sponsorBlockIntroRow,
            [SponsorBlockCategories.Outro] = _sponsorBlockOutroRow,
            [SponsorBlockCategories.Preview] = _sponsorBlockPreviewRow,
            [SponsorBlockCategories.Hook] = _sponsorBlockHookRow,
            [SponsorBlockCategories.Filler] = _sponsorBlockFillerRow
        };
        _shortcutRows = new Dictionary<string, Button>(StringComparer.Ordinal)
        {
            ["TogglePause"] = _shortcutTogglePauseButton,
            ["SeekBackward"] = _shortcutSeekBackwardButton,
            ["SeekForward"] = _shortcutSeekForwardButton,
            ["StepFrameBackward"] = _shortcutStepFrameBackwardButton,
            ["StepFrameForward"] = _shortcutStepFrameForwardButton,
            ["ToggleMute"] = _shortcutToggleMuteButton,
            ["VolumeUp"] = _shortcutVolumeUpButton,
            ["VolumeDown"] = _shortcutVolumeDownButton,
            ["SeekToBeginning"] = _shortcutSeekToBeginningButton,
            ["ReturnToShell"] = _shortcutReturnToShellButton,
            ["ToggleVideoInfo"] = _shortcutToggleVideoInfoButton,
            ["SpeedDecrease"] = _shortcutSpeedDecreaseButton,
            ["SpeedIncrease"] = _shortcutSpeedIncreaseButton,
            ["NextVideo"] = _shortcutNextVideoButton,
            ["PreviousVideo"] = _shortcutPreviousVideoButton,
            ["ToggleFullscreen"] = _shortcutToggleFullscreenButton,
            ["PreferredSubtitle"] = _shortcutPreferredSubtitleButton,
            ["ResumeOrSkip"] = _shortcutResumeOrSkipButton
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