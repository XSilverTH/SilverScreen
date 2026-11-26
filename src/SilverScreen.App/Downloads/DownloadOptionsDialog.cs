using Adw;
using Gtk;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Downloads;
using SilverScreen.Core.Preferences;
using XSTH.Blueprint.Helpers;
using AlertDialog = Adw.AlertDialog;
using Dialog = Adw.Dialog;
using Window = Gtk.Window;

namespace SilverScreen.Downloads;

public partial class DownloadOptionsDialog : ViewBase<Dialog>
{
    private static readonly ILogger Logger = Log.ForContext<DownloadOptionsDialog>();

    private static readonly VideoQuality[] QualityChoices =
    [
        VideoQuality.Best, VideoQuality.P4320, VideoQuality.P2160, VideoQuality.P1440,
        VideoQuality.P1080, VideoQuality.P720, VideoQuality.P480, VideoQuality.P360,
        VideoQuality.P240, VideoQuality.P144
    ];

    private readonly IDownloadService? _downloadService;
    private readonly string? _editingPresetId;
    private readonly bool _isPresetEditor;
    private readonly INotificationService? _notifications;
    private readonly Action<DownloadPreset>? _onSavePreset;
    private readonly IPreferencesService _preferencesService;
    private readonly IReadOnlyList<VideoSummary> _videos;
    private IReadOnlyList<DownloadContainerFormat> _activeContainers = DownloadContainerFormats.VideoContainers;
    private string? _customFolderPath;
    private bool _loading;

    private DownloadOptionsDialog(
        IReadOnlyList<VideoSummary> videos,
        IPreferencesService preferencesService,
        IDownloadService? downloadService,
        INotificationService? notifications,
        DownloadOptions initialOptions,
        bool isPresetEditor,
        string? editingPresetId,
        string? initialPresetName,
        Action<DownloadPreset>? onSavePreset)
    {
        _videos = videos;
        _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        _downloadService = downloadService;
        _notifications = notifications;
        _isPresetEditor = isPresetEditor;
        _editingPresetId = editingPresetId;
        _onSavePreset = onSavePreset;

        ConfigureMode(initialPresetName);
        ApplyOptions(initialOptions);
    }

    public static DownloadOptionsDialog ForDownload(
        IReadOnlyList<VideoSummary> videos,
        IPreferencesService preferencesService,
        IDownloadService downloadService,
        INotificationService? notifications,
        DownloadOptions? initialOptions = null)
    {
        var prefs = preferencesService.GetPreferences();
        var defaults = initialOptions ?? new DownloadOptions
        {
            Quality = prefs.Quality,
            PreferredCodec = prefs.PreferredCodec,
            SubtitleLanguage = prefs.PreferredSubtitleLanguage
        };

        return new DownloadOptionsDialog(
            videos,
            preferencesService,
            downloadService,
            notifications,
            defaults,
            false,
            null,
            null,
            null);
    }

    public static DownloadOptionsDialog ForPreset(
        IPreferencesService preferencesService,
        DownloadPreset? existingPreset,
        Action<DownloadPreset> onSavePreset)
    {
        var prefs = preferencesService.GetPreferences();
        var options = existingPreset?.Options ?? new DownloadOptions
        {
            Quality = prefs.Quality,
            PreferredCodec = prefs.PreferredCodec,
            SubtitleLanguage = prefs.PreferredSubtitleLanguage
        };

        return new DownloadOptionsDialog(
            [],
            preferencesService,
            null,
            null,
            options,
            true,
            existingPreset?.Id,
            existingPreset?.Name ?? string.Empty,
            onSavePreset);
    }

    private void ConfigureMode(string? initialPresetName)
    {
        if (_isPresetEditor)
        {
            Widget.Title = string.IsNullOrWhiteSpace(_editingPresetId) ? "New Download Preset" : "Edit Download Preset";
            confirm_button.Label = "Save";
            preset_name_group.Visible = true;
            ((Editable)preset_name_row).SetText(initialPresetName ?? string.Empty);
            save_preset_footer.Visible = false;
            target_summary_label.Visible = false;
        }
        else
        {
            Widget.Title = _videos.Count == 1 ? "Download Video" : $"Download {_videos.Count} Videos";
            confirm_button.Label = _videos.Count == 1 ? "Download" : $"Download {_videos.Count}";
            preset_name_group.Visible = false;
            save_preset_footer.Visible = true;
            target_summary_label.Visible = _videos.Count > 0;
            target_summary_label.SetText(_videos.Count == 1
                ? _videos[0].Title
                : $"Batch download for {_videos.Count} videos in Watch Queue");
        }
    }

    private void ApplyOptions(DownloadOptions options)
    {
        _loading = true;
        try
        {
            _customFolderPath = options.CustomFolderPath;
            destination_row.Selected = options.DestinationKind == DownloadDestinationKind.CustomFolder ? 1u : 0u;
            UpdateCustomFolderDisplay();

            media_mode_row.Selected = options.MediaMode == DownloadMediaMode.AudioOnly ? 1u : 0u;

            var qIdx = Array.IndexOf(QualityChoices, options.Quality);
            quality_row.Selected = (uint)(qIdx >= 0 ? qIdx : 0);

            codec_row.Selected = options.PreferredCodec switch
            {
                PreferredVideoCodec.Av1 => 1u,
                PreferredVideoCodec.Vp9 => 2u,
                PreferredVideoCodec.H264 => 3u,
                _ => 0u
            };

            RefreshContainerChoices(options.MediaMode, options.Container);

            subtitle_mode_row.Selected = options.SubtitleMode switch
            {
                DownloadSubtitleMode.Embedded => 1u,
                DownloadSubtitleMode.Sidecar => 2u,
                _ => 0u
            };
            ((Editable)subtitle_lang_row).SetText(options.SubtitleLanguage);

            embed_metadata_row.Active = options.EmbedMetadata;
            embed_thumbnail_row.Active = options.EmbedThumbnail;

            sponsorblock_row.Selected = options.SponsorBlockMode switch
            {
                SponsorBlockDownloadMode.Strip => 1u,
                SponsorBlockDownloadMode.Off => 2u,
                _ => 0u
            };

            UpdateDynamicVisibility();
            UpdateConfirmSensitivity();
        }
        finally
        {
            _loading = false;
        }
    }

    private void RefreshContainerChoices(DownloadMediaMode mode, DownloadContainerFormat desired)
    {
        _activeContainers = DownloadContainerFormats.GetForMode(mode);
        var labels = _activeContainers.Select(static c => c.ToDisplayString()).ToArray();
        var model = (StringList)container_row.Model;
        model.Splice(0, model.GetNItems(), labels);

        var normalized = desired.NormalizeForMode(mode);
        var idx = -1;
        for (var i = 0; i < _activeContainers.Count; i++)
            if (_activeContainers[i] == normalized)
            {
                idx = i;
                break;
            }

        container_row.Selected = (uint)Math.Max(0, idx);
    }

    private void UpdateDynamicVisibility()
    {
        var isCustomDest = destination_row.Selected == 1;
        var isAudioOnly = media_mode_row.Selected == 1;
        var hasSubtitles = subtitle_mode_row.Selected != 0;

        custom_folder_row.Visible = isCustomDest;
        // SponsorBlock download processing only applies when downloading to an external folder.
        // Library downloads index segments directly in SilverScreen's database.
        sponsorblock_group.Visible = isCustomDest;

        quality_row.Visible = !isAudioOnly;
        codec_row.Visible = !isAudioOnly;
        subtitle_lang_row.Visible = hasSubtitles;

        var selectedContainer = GetSelectedContainer();
        var isWebmVideo = !isAudioOnly && selectedContainer == DownloadContainerFormat.Webm;
        embed_thumbnail_row.Sensitive = !isWebmVideo;
        embed_thumbnail_row.Subtitle = isWebmVideo ? "Not supported in WebM video containers" : string.Empty;
    }

    private void UpdateCustomFolderDisplay()
    {
        custom_folder_row.Subtitle = string.IsNullOrWhiteSpace(_customFolderPath)
            ? "No folder selected"
            : _customFolderPath;
    }

    private void UpdateConfirmSensitivity()
    {
        var destOk = destination_row.Selected == 0 || !string.IsNullOrWhiteSpace(_customFolderPath);
        var nameOk = !_isPresetEditor || !string.IsNullOrWhiteSpace(((Editable)preset_name_row).GetText());
        confirm_button.Sensitive = destOk && nameOk;
        save_preset_button.Sensitive = destOk;
    }

    private DownloadContainerFormat GetSelectedContainer()
    {
        var idx = (int)container_row.Selected;
        return idx >= 0 && idx < _activeContainers.Count
            ? _activeContainers[idx]
            : DownloadContainerFormat.Auto;
    }

    private DownloadOptions BuildOptions()
    {
        var dest = destination_row.Selected == 1
            ? DownloadDestinationKind.CustomFolder
            : DownloadDestinationKind.Library;
        var mode = media_mode_row.Selected == 1
            ? DownloadMediaMode.AudioOnly
            : DownloadMediaMode.VideoAndAudio;
        var qualityIdx = (int)quality_row.Selected;
        var quality = qualityIdx >= 0 && qualityIdx < QualityChoices.Length
            ? QualityChoices[qualityIdx]
            : VideoQuality.Best;
        var codec = codec_row.Selected switch
        {
            1 => PreferredVideoCodec.Av1,
            2 => PreferredVideoCodec.Vp9,
            3 => PreferredVideoCodec.H264,
            _ => PreferredVideoCodec.Any
        };
        var subMode = subtitle_mode_row.Selected switch
        {
            1 => DownloadSubtitleMode.Embedded,
            2 => DownloadSubtitleMode.Sidecar,
            _ => DownloadSubtitleMode.None
        };
        var sbMode = sponsorblock_row.Selected switch
        {
            1 => SponsorBlockDownloadMode.Strip,
            2 => SponsorBlockDownloadMode.Off,
            _ => SponsorBlockDownloadMode.MarkAsChapters
        };

        return new DownloadOptions
        {
            DestinationKind = dest,
            CustomFolderPath = dest == DownloadDestinationKind.CustomFolder ? _customFolderPath : null,
            MediaMode = mode,
            Quality = quality,
            PreferredCodec = codec,
            Container = GetSelectedContainer(),
            SubtitleMode = subMode,
            SubtitleLanguage = ((Editable)subtitle_lang_row).GetText(),
            EmbedMetadata = embed_metadata_row.Active,
            EmbedThumbnail = embed_thumbnail_row.Active,
            SponsorBlockMode = sbMode
        }.Normalized();
    }

    private void OnPresetNameChanged(object? sender, EventArgs args)
    {
        if (_loading) return;
        UpdateConfirmSensitivity();
    }

    private void OnDestinationChanged(object? sender, EventArgs args)
    {
        if (_loading) return;
        UpdateDynamicVisibility();
        UpdateConfirmSensitivity();
    }

    private void OnMediaModeChanged(object? sender, EventArgs args)
    {
        if (_loading) return;
        var mode = media_mode_row.Selected == 1
            ? DownloadMediaMode.AudioOnly
            : DownloadMediaMode.VideoAndAudio;
        _loading = true;
        try
        {
            RefreshContainerChoices(mode, DownloadContainerFormat.Auto);
        }
        finally
        {
            _loading = false;
        }

        UpdateDynamicVisibility();
    }

    private void OnContainerChanged(object? sender, EventArgs args)
    {
        if (_loading) return;
        UpdateDynamicVisibility();
    }

    private void OnSubtitleModeChanged(object? sender, EventArgs args)
    {
        if (_loading) return;
        UpdateDynamicVisibility();
    }

    private void OnBrowseFolderClicked(object? sender, EventArgs args)
    {
        var chooser = FileChooserNative.New(
            "Choose download folder",
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
                _customFolderPath = path;
                UpdateCustomFolderDisplay();
                UpdateConfirmSensitivity();
            }
            finally
            {
                chooser.Dispose();
            }
        };
        chooser.Show();
    }

    private void OnSaveAsPresetClicked(object? sender, EventArgs args)
    {
        var options = BuildOptions();
        var prompt = AlertDialog.New("Save Download Preset", "Give this set of download options a short name.");
        prompt.AddResponse("cancel", "Cancel");
        prompt.AddResponse("save", "Save");
        prompt.SetResponseAppearance("save", ResponseAppearance.Suggested);
        prompt.DefaultResponse = "save";
        prompt.CloseResponse = "cancel";

        var entry = Entry.New();
        entry.PlaceholderText = "e.g. 1080p Library or Audio MP3";
        entry.SetActivatesDefault(true);
        prompt.ExtraChild = entry;

        prompt.OnResponse += (_, responseArgs) =>
        {
            if (!string.Equals(responseArgs.Response, "save", StringComparison.Ordinal))
                return;

            var name = entry.GetText().Trim();
            if (string.IsNullOrWhiteSpace(name))
                return;

            var prefs = _preferencesService.GetPreferences();
            var preset = new DownloadPreset(Guid.NewGuid().ToString("N"), name, options);
            var updated = new List<DownloadPreset>(prefs.DownloadPresets) { preset };
            _preferencesService.SavePreferences(prefs with
            {
                DownloadPresets = new EquatableArray<DownloadPreset>([.. updated])
            });
            _notifications?.ShowToast($"Saved preset “{name}”");
        };

        prompt.Present(Widget);
    }

    private void OnCancelClicked(object? sender, EventArgs args)
    {
        Widget.Close();
    }

    private void OnConfirmClicked(object? sender, EventArgs args)
    {
        var options = BuildOptions();
        if (_isPresetEditor)
        {
            var name = ((Editable)preset_name_row).GetText().Trim();
            if (string.IsNullOrWhiteSpace(name))
                return;

            var id = string.IsNullOrWhiteSpace(_editingPresetId) ? Guid.NewGuid().ToString("N") : _editingPresetId;
            _onSavePreset?.Invoke(new DownloadPreset(id, name, options));
            Widget.Close();
            return;
        }

        if (_downloadService is not null)
            DownloadActionHelper.EnqueueAsync(_videos, options, null, _downloadService, _notifications)
                .FireAndForget(Logger);

        Widget.Close();
    }
}