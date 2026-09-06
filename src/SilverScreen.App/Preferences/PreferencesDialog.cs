using Adw;
using Gtk;
using Serilog;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using XSTH.Blueprint.Helpers;
using Functions = Gdk.Functions;

namespace SilverScreen.Preferences;

public partial class PreferencesDialog : ViewBase<Adw.PreferencesDialog>, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<PreferencesDialog>();
    private readonly IReadOnlyDictionary<string, Button> _shortcutRows;
    private readonly Dictionary<string, string[]> _shortcutValues = new(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, SwitchRow> _sponsorBlockCategoryRows;
    private readonly PreferencesViewModel _viewModel;
    private string? _capturingShortcut;
    private bool _loading;
    private readonly EventControllerKey _keyController;
    private bool _disposed;
    public event EventHandler<string>? SaveFailed;

    public PreferencesDialog(IPreferencesService preferencesService)
    {
        Logger.Information("Opening PreferencesDialog");
        _viewModel = new PreferencesViewModel(preferencesService);
        _sponsorBlockCategoryRows = new Dictionary<string, SwitchRow>
        {
            [SponsorBlockCategories.Sponsor] = sponsorblock_sponsor_row,
            [SponsorBlockCategories.SelfPromotion] = sponsorblock_selfpromo_row,
            [SponsorBlockCategories.InteractionReminder] = sponsorblock_interaction_row,
            [SponsorBlockCategories.Intro] = sponsorblock_intro_row,
            [SponsorBlockCategories.Outro] = sponsorblock_outro_row,
            [SponsorBlockCategories.Preview] = sponsorblock_preview_row,
            [SponsorBlockCategories.Hook] = sponsorblock_hook_row,
            [SponsorBlockCategories.Filler] = sponsorblock_filler_row
        };
        _shortcutRows = new Dictionary<string, Button>(StringComparer.Ordinal)
        {
            ["TogglePause"] = shortcut_toggle_pause_button,
            ["SeekBackward"] = shortcut_seek_backward_button,
            ["SeekForward"] = shortcut_seek_forward_button,
            ["StepFrameBackward"] = shortcut_step_frame_backward_button,
            ["StepFrameForward"] = shortcut_step_frame_forward_button,
            ["ToggleMute"] = shortcut_toggle_mute_button,
            ["VolumeUp"] = shortcut_volume_up_button,
            ["VolumeDown"] = shortcut_volume_down_button,
            ["SeekToBeginning"] = shortcut_seek_to_beginning_button,
            ["ReturnToShell"] = shortcut_return_to_shell_button,
            ["ToggleStats"] = shortcut_toggle_stats_button,
            ["ToggleVideoInfo"] = shortcut_toggle_video_info_button,
            ["ToggleQueue"] = shortcut_toggle_queue_button,
            ["SpeedDecrease"] = shortcut_speed_decrease_button,
            ["SpeedIncrease"] = shortcut_speed_increase_button,
            ["NextVideo"] = shortcut_next_video_button,
            ["PreviousVideo"] = shortcut_previous_video_button,
            ["ToggleFullscreen"] = shortcut_toggle_fullscreen_button,
            ["PreferredSubtitle"] = shortcut_preferred_subtitle_button,
            ["ResumeOrSkip"] = shortcut_resume_or_skip_button
        };

        foreach (var button in _shortcutRows.Values)
            button.OnClicked += OnShortcutButtonClicked;

        _keyController = EventControllerKey.New();
        _keyController.SetPropagationPhase(PropagationPhase.Capture);
        _keyController.OnKeyPressed += OnKeyPressed;
        Widget.AddController(_keyController);
        Widget.OnClosed += OnClosed;

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
            theme_row.Selected = (uint)GetSelectionIndex(theme_model, state.Theme);
            quality_row.Selected = (uint)GetSelectionIndex(quality_model, state.VideoQuality);
            playback_backend_row.Selected = (uint)(PlaybackBackends.IsEmbedded(state.PlaybackBackend) ? 1 : 0);
            fullscreen_row.Active = state.OpenInFullscreen;
            auto_advance_next_video_row.Active = state.AutoAdvanceNextVideo;
            ((Editable)ytdlp_path_row).SetText(state.YtDlpExecutablePath);
            ((Editable)mpv_path_row).SetText(state.MpvExecutablePath);
            ((Editable)subtitle_language_row).SetText(state.PreferredSubtitleLanguage);
            mark_watched_row.Active = state.MarkWatchedVideos;
            youtube_playback_telemetry_row.Active = state.YouTubePlaybackTelemetryEnabled;
            discord_rich_presence_row.Active = state.DiscordRichPresenceEnabled;
            ryd_enabled_row.Active = state.RydEnabled;
            sponsorblock_auto_skip_row.Active = state.SponsorBlockAutoSkipEnabled;
            sponsorblock_display_row.Active = state.SponsorBlockSegmentDisplayEnabled;
            resume_playback_automatically_row.Active = state.ResumePlaybackAutomatically;
            resume_playback_on_demand_row.Active = state.ResumePlaybackOnDemand;
            shortcut_osd_enabled_row.Active = state.ShortcutOsdEnabled;
            ApplyShortcuts(state.Shortcuts);
            foreach (var (category, row) in _sponsorBlockCategoryRows)
                row.Active = state.SponsorBlockCategories.Contains(category, StringComparer.Ordinal);
            UpdatePathStatus();
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
        SetShortcut("ToggleStats", shortcuts.ToggleStats);
        SetShortcut("ToggleVideoInfo", shortcuts.ToggleVideoInfo);
        SetShortcut("ToggleQueue", shortcuts.ToggleQueue);
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
        _shortcutRows[name].SetLabel(values.Length == 0 ? "Unassigned" : string.Join(" / ", values.Select(GetShortcutLabel)));
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

        var changedOption = ReferenceEquals(sender, mark_watched_row)
            ? PreferencesMutuallyExclusiveOption.MarkWatchedVideos
            : ReferenceEquals(sender, youtube_playback_telemetry_row)
                ? PreferencesMutuallyExclusiveOption.YouTubePlaybackTelemetry
                : ReferenceEquals(sender, resume_playback_automatically_row)
                    ? PreferencesMutuallyExclusiveOption.ResumePlaybackAutomatically
                    : ReferenceEquals(sender, resume_playback_on_demand_row)
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
        {
            var message = result.ErrorMessage ?? PreferencesViewModel.PersistenceErrorMessage;
            Logger.Warning("Failed to persist preferences: {Error}", message);
            SaveFailed?.Invoke(this, message);
        }
    }
    private void UpdatePathStatus()
    {
        SetPathStatus(ytdlp_status_row, ytdlp_status_label,
            PreferencesViewModel.ValidateExecutablePath(((Editable)ytdlp_path_row).GetText()));
        SetPathStatus(mpv_status_row, mpv_status_label,
            PreferencesViewModel.ValidateExecutablePath(((Editable)mpv_path_row).GetText()));
    }

    private static void SetPathStatus(ActionRow row, Label label, ExecutablePathStatus status)
    {
        row.SetSubtitle(status.Detail);
        label.SetLabel(status.Found ? "Found" : "Not found");
        label.RemoveCssClass("success");
        label.RemoveCssClass("error");
        label.AddCssClass(status.Found ? "success" : "error");
    }

    private void OnBrowseYtDlpClicked(object? sender, EventArgs args)
    {
        BrowseForExecutable("Choose yt-dlp executable",
            path => ((Editable)ytdlp_path_row).SetText(path));
    }

    private void OnBrowseMpvClicked(object? sender, EventArgs args)
    {
        BrowseForExecutable("Choose mpv executable",
            path => ((Editable)mpv_path_row).SetText(path));
    }

    private void BrowseForExecutable(string title, Action<string> applyPath)
    {
        var chooser = FileChooserNative.New(title, null, FileChooserAction.Open, "_Open", "_Cancel");
        chooser.OnResponse += (_, response) =>
        {
            try
            {
                if (response.ResponseId == (int)ResponseType.Accept)
                {
                    var path = chooser.GetFile()?.GetPath();
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        applyPath(path);
                        Save();
                    }
                }
            }
            finally
            {
                chooser.Dispose();
            }
        };
        chooser.Show();
    }

    private PreferencesEditorState CreateEditorState()
    {
        return _viewModel.EditorState with
        {
            Theme = GetSelectedValue(theme_model, theme_row.Selected, "System"),
            VideoQuality = GetSelectedValue(quality_model, quality_row.Selected, "Best"),
            YtDlpExecutablePath = ((Editable)ytdlp_path_row).GetText(),
            MpvExecutablePath = ((Editable)mpv_path_row).GetText(),
            PlaybackBackend = playback_backend_row.Selected == 1
                ? PlaybackBackends.EmbeddedPlayer
                : PlaybackBackends.ExternalMpv,
            OpenInFullscreen = fullscreen_row.Active,
            PreferredSubtitleLanguage = ((Editable)subtitle_language_row).GetText(),
            AutoAdvanceNextVideo = auto_advance_next_video_row.Active,
            MarkWatchedVideos = mark_watched_row.Active,
            YouTubePlaybackTelemetryEnabled = youtube_playback_telemetry_row.Active,
            DiscordRichPresenceEnabled = discord_rich_presence_row.Active,
            RydEnabled = ryd_enabled_row.Active,
            SponsorBlockAutoSkipEnabled = sponsorblock_auto_skip_row.Active,
            SponsorBlockSegmentDisplayEnabled = sponsorblock_display_row.Active,
            ResumePlaybackAutomatically = resume_playback_automatically_row.Active,
            ResumePlaybackOnDemand = resume_playback_on_demand_row.Active,
            ShortcutOsdEnabled = shortcut_osd_enabled_row.Active,
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
            ToggleStats = ReadShortcut("ToggleStats"),
            ToggleVideoInfo = ReadShortcut("ToggleVideoInfo"),
            ToggleQueue = ReadShortcut("ToggleQueue"),
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

    private bool OnKeyPressed(EventControllerKey sender, EventControllerKey.KeyPressedSignalArgs args)
    {
        return CaptureShortcut(args.Keyval);
    }

    private void OnClosed(Adw.Dialog sender, EventArgs args)
    {
        Dispose();
    }

    public new void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Widget.OnClosed -= OnClosed;

        _keyController.OnKeyPressed -= OnKeyPressed;
        Widget.RemoveController(_keyController);
        _keyController.Dispose();

        foreach (var button in _shortcutRows.Values)
            button.OnClicked -= OnShortcutButtonClicked;

        _capturingShortcut = null;
        SaveFailed = null;

        base.Dispose();
        Builder.Dispose();
        Widget.Dispose();
    }
}