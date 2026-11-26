using System.Globalization;
using Gio;
using Gtk;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Downloads;
using SilverScreen.Core.Preferences;
using SilverScreen.Shell.Notifications;
using Action = System.Action;
using Menu = Gio.Menu;
using Task = System.Threading.Tasks.Task;

namespace SilverScreen.Downloads;

public enum DownloadTriggerBehavior
{
    OpenDialog = 0,
    ImmediatePreset = 1,
    ShowPresetMenu = 2
}

/// <summary>
///     Shared adaptive download menu/trigger coordinator used across video cards,
///     the embedded player header bar, and the Watch Queue footer.
/// </summary>
public static class DownloadActionHelper
{
    private static readonly ILogger Logger = Log.ForContext(typeof(DownloadActionHelper));

    public static DownloadTriggerBehavior GetBehavior(AppPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var count = preferences.DownloadPresets.Length;
        if (count == 0)
            return DownloadTriggerBehavior.OpenDialog;
        if (count == 1 && !preferences.ShowCustomDownloadOption)
            return DownloadTriggerBehavior.ImmediatePreset;
        return DownloadTriggerBehavior.ShowPresetMenu;
    }

    public static void HandleButtonTrigger(
        Widget anchorWidget,
        Func<IReadOnlyList<VideoSummary>> getVideos,
        IPreferencesService preferencesService,
        IDownloadService downloadService,
        INotificationService? notifications,
        ref PopoverMenu? popoverMenu,
        ref SimpleActionGroup? actionGroup)
    {
        var videos = getVideos();
        if (videos.Count == 0)
            return;

        var prefs = preferencesService.GetPreferences();
        switch (GetBehavior(prefs))
        {
            case DownloadTriggerBehavior.OpenDialog:
                PresentOptionsDialog(anchorWidget, videos, preferencesService, downloadService, notifications);
                break;

            case DownloadTriggerBehavior.ImmediatePreset:
                var preset = prefs.DownloadPresets[0];
                EnqueueAsync(videos, preset.Options, preset.Name, downloadService, notifications)
                    .FireAndForget(Logger);
                break;

            case DownloadTriggerBehavior.ShowPresetMenu:
                ShowPresetPopover(anchorWidget, getVideos, preferencesService, downloadService, notifications,
                    ref popoverMenu, ref actionGroup);
                break;
        }
    }

    public static void PopulateMenuSection(
        Menu targetMenu,
        string actionNamespace,
        AppPreferences preferences,
        List<SimpleAction> createdActions,
        SimpleActionGroup actionGroup,
        Func<IReadOnlyList<VideoSummary>> getVideos,
        Widget parentWidget,
        IPreferencesService preferencesService,
        IDownloadService downloadService,
        INotificationService? notifications)
    {
        var presets = preferences.DownloadPresets;
        for (var i = 0; i < presets.Length; i++)
        {
            var preset = presets[i];
            var actionName = $"dl-preset-{i}";
            var action = SimpleAction.New(actionName, null);
            action.OnActivate += (_, _) =>
            {
                var videos = getVideos();
                if (videos.Count > 0)
                    EnqueueAsync(videos, preset.Options, preset.Name, downloadService, notifications)
                        .FireAndForget(Logger);
            };
            actionGroup.AddAction(action);
            createdActions.Add(action);
            targetMenu.Append(preset.Name, $"{actionNamespace}.{actionName}");
        }

        if (preferences.ShowCustomDownloadOption)
        {
            var customAction = SimpleAction.New("dl-custom", null);
            customAction.OnActivate += (_, _) =>
            {
                var videos = getVideos();
                if (videos.Count > 0)
                    PresentOptionsDialog(parentWidget, videos, preferencesService, downloadService, notifications);
            };
            actionGroup.AddAction(customAction);
            createdActions.Add(customAction);
            targetMenu.Append("Custom…", $"{actionNamespace}.dl-custom");
        }
    }

    public static void PresentOptionsDialog(
        Widget parentWidget,
        IReadOnlyList<VideoSummary> videos,
        IPreferencesService preferencesService,
        IDownloadService downloadService,
        INotificationService? notifications,
        DownloadOptions? initialOptions = null)
    {
        if (videos.Count == 0)
            return;

        var dialog = DownloadOptionsDialog.ForDownload(
            videos,
            preferencesService,
            downloadService,
            notifications,
            initialOptions);
        dialog.Widget.Present(parentWidget);
    }

    public static async Task EnqueueAsync(
        IReadOnlyList<VideoSummary> videos,
        DownloadOptions options,
        string? presetName,
        IDownloadService downloadService,
        INotificationService? notifications)
    {
        try
        {
            var result = await downloadService
                .EnqueueAsync(new DownloadEnqueueRequest(videos, options, presetName))
                .ConfigureAwait(false);
            notifications?.ShowToast(result.SummaryMessage);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to enqueue download");
            notifications?.ShowToast("Could not start download. Try again.");
        }
    }

    private static void ShowPresetPopover(
        Widget anchorWidget,
        Func<IReadOnlyList<VideoSummary>> getVideos,
        IPreferencesService preferencesService,
        IDownloadService downloadService,
        INotificationService? notifications,
        ref PopoverMenu? popoverMenu,
        ref SimpleActionGroup? actionGroup)
    {
        if (popoverMenu is not null)
        {
            popoverMenu.Popdown();
            popoverMenu.Unparent();
            popoverMenu.Dispose();
            popoverMenu = null;
        }

        actionGroup?.Dispose();
        actionGroup = SimpleActionGroup.New();

        var menu = Menu.New();
        var createdActions = new List<SimpleAction>();
        PopulateMenuSection(
            menu,
            "dlpop",
            preferencesService.GetPreferences(),
            createdActions,
            actionGroup,
            getVideos,
            anchorWidget,
            preferencesService,
            downloadService,
            notifications);

        popoverMenu = PopoverMenu.NewFromModel(menu);
        popoverMenu.SetParent(anchorWidget);
        popoverMenu.InsertActionGroup("dlpop", actionGroup);
        popoverMenu.Popup();
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:0.#} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:0.#} MB";
        double gb = mb / 1024.0;
        return $"{gb:0.##} GB";
    }

    public static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0) return string.Empty;
        return $"{FormatBytes((long)bytesPerSecond)}/s";
    }

    public static string FormatEta(TimeSpan eta)
    {
        if (eta.TotalHours >= 1)
            return $"{(int)eta.TotalHours}:{eta.Minutes:00}:{eta.Seconds:00}";
        return $"{(int)eta.TotalMinutes}:{eta.Seconds:00}";
    }
}
