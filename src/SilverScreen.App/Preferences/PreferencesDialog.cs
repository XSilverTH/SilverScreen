using Adw;
using Gtk;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Downloads;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Downloads;
using XSTH.Blueprint.Helpers;
using Dialog = Adw.Dialog;
using Functions = Gdk.Functions;
using Window = Gtk.Window;

namespace SilverScreen.Preferences;

public partial class PreferencesDialog : ViewBase<Adw.PreferencesDialog>
{
    private static readonly ILogger Logger = Log.ForContext<PreferencesDialog>();
    private readonly IPreferencesService _preferencesService;
    private readonly List<ActionRow> _presetRows = [];
    private readonly IReadOnlyDictionary<string, Button> _shortcutRows;
    private readonly Dictionary<string, string[]> _shortcutValues = new(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, SwitchRow> _sponsorBlockCategoryRows;
    private readonly PreferencesViewModel _viewModel;
    private string? _capturingShortcut;
    private string? _lastValidatedMpvPath;
    private string? _lastValidatedYtDlpPath;
    private string _libraryDirectory = AppPreferences.GetDefaultLibraryDirectory();
    private bool _loading;
    private CancellationTokenSource? _pathValidationCts;

    public PreferencesDialog(IPreferencesService preferencesService)
    {
        _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
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
            Lifetime.Track(() => button.OnClicked += OnShortcutButtonClicked,
                () => button.OnClicked -= OnShortcutButtonClicked);

        var keyController = EventControllerKey.New();
        keyController.SetPropagationPhase(PropagationPhase.Capture);
        Lifetime.Attach(Widget, keyController,
            controller => controller.OnKeyPressed += OnKeyPressed,
            controller => controller.OnKeyPressed -= OnKeyPressed);
        Lifetime.Track(() => Widget.OnClosed += OnClosed, () => Widget.OnClosed -= OnClosed);

        InitializeFields();
    }

    public event EventHandler<string>? SaveFailed;


    private void InitializeFields()
    {
        ApplyEditorState(_viewModel.EditorState);
    }

    private void ApplyEditorState(PreferencesEditorState state)
    {
        _loading = true;
        try
        {
            theme_row.Selected = (uint)state.Theme;
            quality_row.Selected = QualityToRowIndex(state.VideoQuality);
            preferred_codec_row.Selected = (uint)state.PreferredCodec;
            playback_backend_row.Selected =
                state.PlaybackBackend == PlaybackBackendKind.Embedded ? 1u : 0u;
            fullscreen_row.Active = state.OpenInFullscreen;
            auto_advance_next_video_row.Active = state.AutoAdvanceNextVideo;
            ((Editable)ytdlp_path_row).SetText(state.YtDlpExecutablePath);
            ((Editable)mpv_path_row).SetText(state.MpvExecutablePath);
            ((Editable)subtitle_language_row).SetText(state.PreferredSubtitleLanguage);
            if (!string.Equals(((Editable)youtube_language_row).GetText(), state.YouTubeLanguage,
                    StringComparison.Ordinal))
                ((Editable)youtube_language_row).SetText(state.YouTubeLanguage);
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
            _libraryDirectory = state.LibraryDirectory;
            library_folder_row.Subtitle = state.LibraryDirectory;
            library_folder_reset_button.Sensitive = !string.Equals(
                state.LibraryDirectory,
                AppPreferences.GetDefaultLibraryDirectory(),
                StringComparison.Ordinal);
            max_concurrent_downloads_row.SetValue(state.MaxConcurrentDownloads);
            show_custom_download_row.Active = state.ShowCustomDownloadOption;
            RebuildPresetRows(state.DownloadPresets);
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
        _shortcutRows[name]
            .SetLabel(values.Length == 0 ? "Unassigned" : string.Join(" / ", values.Select(GetShortcutLabel)));
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

    private static ThemeMode GetSelectedTheme(uint selected)
    {
        return selected <= (uint)ThemeMode.Dark ? (ThemeMode)selected : ThemeMode.System;
    }

    private static uint QualityToRowIndex(VideoQuality quality)
    {
        return quality switch
        {
            VideoQuality.P4320 => 1,
            VideoQuality.P2160 => 2,
            VideoQuality.P1440 => 3,
            VideoQuality.P1080 => 4,
            VideoQuality.P720 => 5,
            VideoQuality.P480 => 6,
            VideoQuality.P360 => 7,
            VideoQuality.P240 => 8,
            VideoQuality.P144 => 9,
            _ => 0
        };
    }

    private static VideoQuality GetSelectedQuality(uint selected)
    {
        return selected switch
        {
            1 => VideoQuality.P4320,
            2 => VideoQuality.P2160,
            3 => VideoQuality.P1440,
            4 => VideoQuality.P1080,
            5 => VideoQuality.P720,
            6 => VideoQuality.P480,
            7 => VideoQuality.P360,
            8 => VideoQuality.P240,
            9 => VideoQuality.P144,
            _ => VideoQuality.Best
        };
    }

    private static PreferredVideoCodec GetSelectedCodec(uint selected)
    {
        return selected <= (uint)PreferredVideoCodec.H264
            ? (PreferredVideoCodec)selected
            : PreferredVideoCodec.Any;
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
        if (result.Succeeded) return;
        var message = result.ErrorMessage ?? PreferencesViewModel.PersistenceErrorMessage;
        Logger.Warning("Failed to persist preferences: {Error}", message);
        SaveFailed?.Invoke(this, message);
    }

    private void UpdatePathStatus()
    {
        var ytDlpPath = ((Editable)ytdlp_path_row).GetText();
        var mpvPath = ((Editable)mpv_path_row).GetText();
        if (string.Equals(_lastValidatedYtDlpPath, ytDlpPath, StringComparison.Ordinal) &&
            string.Equals(_lastValidatedMpvPath, mpvPath, StringComparison.Ordinal))
            return;

        _lastValidatedYtDlpPath = ytDlpPath;
        _lastValidatedMpvPath = mpvPath;
        _pathValidationCts?.Cancel();
        _pathValidationCts?.Dispose();
        var cts = new CancellationTokenSource();
        _pathValidationCts = cts;
        ValidatePathsAsync(ytDlpPath, mpvPath, cts.Token).FireAndForget(Logger);
    }

    private async Task ValidatePathsAsync(string ytDlpPath, string mpvPath, CancellationToken cancellationToken)
    {
        var (ytDlpStatus, mpvStatus) = await Task.Run(() => (
            PreferencesViewModel.ValidateExecutablePath(ytDlpPath),
            PreferencesViewModel.ValidateExecutablePath(mpvPath)), cancellationToken).ConfigureAwait(false);

        if (IsDisposed || cancellationToken.IsCancellationRequested)
            return;

        GLib.Functions.IdleAdd(0, () =>
        {
            if (IsDisposed || cancellationToken.IsCancellationRequested)
                return false;

            SetPathStatus(ytdlp_status_row, ytdlp_status_label, ytDlpStatus);
            SetPathStatus(mpv_status_row, mpv_status_label, mpvStatus);
            return false;
        });
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
                if (response.ResponseId != (int)ResponseType.Accept) return;
                var path = chooser.GetFile()?.GetPath();
                if (string.IsNullOrWhiteSpace(path)) return;
                applyPath(path);
                Save();
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
            Theme = GetSelectedTheme(theme_row.Selected),
            VideoQuality = GetSelectedQuality(quality_row.Selected),
            PreferredCodec = GetSelectedCodec(preferred_codec_row.Selected),
            YtDlpExecutablePath = ((Editable)ytdlp_path_row).GetText(),
            MpvExecutablePath = ((Editable)mpv_path_row).GetText(),
            PlaybackBackend = playback_backend_row.Selected == 1
                ? PlaybackBackendKind.Embedded
                : PlaybackBackendKind.ExternalMpv,
            OpenInFullscreen = fullscreen_row.Active,
            PreferredSubtitleLanguage = ((Editable)subtitle_language_row).GetText(),
            YouTubeLanguage = ((Editable)youtube_language_row).GetText(),
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
            ],
            LibraryDirectory = _libraryDirectory,
            MaxConcurrentDownloads = Math.Clamp((int)Math.Round(max_concurrent_downloads_row.GetValue()),
                AppPreferences.MinConcurrentDownloads, AppPreferences.MaxConcurrentDownloadsLimit),
            ShowCustomDownloadOption = show_custom_download_row.Active
        };
    }

    private void RebuildPresetRows(EquatableArray<DownloadPreset> presets)
    {
        foreach (var row in _presetRows)
            download_presets_group.Remove(row);
        _presetRows.Clear();

        if (presets.Length == 0)
        {
            var emptyRow = ActionRow.New();
            emptyRow.Title = "No presets saved";
            emptyRow.Subtitle = "Add a preset here or click “Save as preset…” in the download dialog";
            emptyRow.Sensitive = false;
            download_presets_group.Add(emptyRow);
            _presetRows.Add(emptyRow);
            return;
        }

        foreach (var preset in presets)
        {
            var row = ActionRow.New();
            row.Title = preset.Name;
            row.Subtitle = preset.Options.FormatSummary();

            var editBtn = Button.NewFromIconName("document-edit-symbolic");
            editBtn.TooltipText = "Edit preset";
            editBtn.Valign = Align.Center;
            editBtn.AddCssClass("flat");
            editBtn.AddCssClass("square");
            editBtn.OnClicked += (_, _) => OpenPresetEditor(preset);

            var deleteBtn = Button.NewFromIconName("user-trash-symbolic");
            deleteBtn.TooltipText = "Delete preset";
            deleteBtn.Valign = Align.Center;
            deleteBtn.AddCssClass("flat");
            deleteBtn.AddCssClass("square");
            deleteBtn.OnClicked += (_, _) => DeletePreset(preset.Id);

            row.AddSuffix(editBtn);
            row.AddSuffix(deleteBtn);
            download_presets_group.Add(row);
            _presetRows.Add(row);
        }
    }

    private void OnBrowseLibraryFolderClicked(object? sender, EventArgs args)
    {
        var chooser = FileChooserNative.New(
            "Choose Library folder",
            Widget.GetRoot() as Window,
            FileChooserAction.SelectFolder,
            "_Select",
            "_Cancel");
        chooser.OnResponse += (_, response) =>
        {
            try
            {
                if (response.ResponseId != (int)ResponseType.Accept) return;
                var path = chooser.GetFile()?.GetPath();
                if (string.IsNullOrWhiteSpace(path)) return;
                _libraryDirectory = path;
                Save();
            }
            finally
            {
                chooser.Dispose();
            }
        };
        chooser.Show();
    }

    private void OnResetLibraryFolderClicked(object? sender, EventArgs args)
    {
        _libraryDirectory = AppPreferences.GetDefaultLibraryDirectory();
        Save();
    }

    private void OnAddPresetClicked(object? sender, EventArgs args)
    {
        OpenPresetEditor(null);
    }

    private void OpenPresetEditor(DownloadPreset? existing)
    {
        var dialog = DownloadOptionsDialog.ForPreset(_preferencesService, existing, saved =>
        {
            var list = new List<DownloadPreset>(_viewModel.EditorState.DownloadPresets);
            var idx = list.FindIndex(p => string.Equals(p.Id, saved.Id, StringComparison.Ordinal));
            if (idx >= 0)
                list[idx] = saved;
            else
                list.Add(saved);

            var nextState = CreateEditorState() with
            {
                DownloadPresets = new EquatableArray<DownloadPreset>([.. list])
            };
            var result = _viewModel.Save(nextState);
            ApplyEditorState(result.State);
        });
        dialog.Widget.Present(Widget);
    }

    private void DeletePreset(string presetId)
    {
        var list = _viewModel.EditorState.DownloadPresets
            .Where(p => !string.Equals(p.Id, presetId, StringComparison.Ordinal))
            .ToArray();
        var nextState = CreateEditorState() with
        {
            DownloadPresets = new EquatableArray<DownloadPreset>(list)
        };
        var result = _viewModel.Save(nextState);
        ApplyEditorState(result.State);
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

    private void OnClosed(Dialog sender, EventArgs args)
    {
        Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pathValidationCts?.Cancel();
            _pathValidationCts?.Dispose();
            _pathValidationCts = null;
        }

        base.Dispose(disposing);
    }
}