using Gdk;
using Gio;
using Gtk;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Player;
using SilverScreen.Core.Queue;
using SilverScreen.Shell;
using Action = System.Action;
using Task = System.Threading.Tasks.Task;

namespace SilverScreen.Browsing.Components;

public sealed class VideoCardContextMenu : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<VideoCardContextMenu>();
    private readonly INavigationService _navigation;
    private readonly IPlaybackService _playback;
    private readonly IQueueService _queue;
    private readonly INotificationService _notifications;
    private readonly SimpleActionGroup _actionGroup = SimpleActionGroup.New();
    private readonly SimpleAction[] _actions;
    private PopoverMenu? _popoverMenu;
    private Widget? _parentWidget;
    private VideoSummary? _video;

    public VideoCardContextMenu(INavigationService navigation, IPlaybackService playback, IQueueService queue, INotificationService notifications)
    {
        _navigation = navigation; _playback = playback; _queue = queue; _notifications = notifications;
        _actions = [
            CreateAction("play", () => { if (_video is { } v) Play(v); }),
            CreateAction("play-alternate", () => { if (_video is { } v) PlayAlternate(v); }),
            CreateAction("add-to-queue", () => { if (_video is { } v) AddToQueue(v); }),
            CreateAction("open-channel", () => { if (_video is { } v) OpenChannel(v); }),
            CreateAction("copy-link", () => { if (_video is { } v) CopyLink(v); })
        ];
        foreach (var action in _actions) _actionGroup.AddAction(action);
    }

    public void AttachTo(Widget parentWidget, MenuButton menuButton)
    {
        _parentWidget = parentWidget;
        var menuModel = Menu.New();
        foreach (var (t, a) in new[] { ("Play", "video.play"), ("Open in other player", "video.play-alternate"), ("Add to queue", "video.add-to-queue"), ("Open channel", "video.open-channel"), ("Copy link", "video.copy-link") })
            menuModel.Append(t, a);
        menuButton.MenuModel = menuModel;
        menuButton.InsertActionGroup("video", _actionGroup);
        parentWidget.InsertActionGroup("video", _actionGroup);
        _popoverMenu = PopoverMenu.NewFromModel(menuModel);
        _popoverMenu.HasArrow = false;
        _popoverMenu.SetParent(parentWidget);
        _popoverMenu.InsertActionGroup("video", _actionGroup);
    }

    public void SetVideo(VideoSummary? video) => _video = video;
    public void Dismiss() => _popoverMenu?.Popdown();

    public void ShowAt(int x = -1, int y = -1)
    {
        if (_video is null || _popoverMenu is null || _parentWidget is null) return;
        _popoverMenu.SetPointingTo(new Rectangle { X = x >= 0 ? x : Math.Max(0, _parentWidget.GetWidth() / 2), Y = y >= 0 ? y : Math.Max(0, _parentWidget.GetHeight() / 2), Width = 1, Height = 1 });
        _popoverMenu.Popup();
    }

    public void Play(VideoSummary video) => PlayInternalAsync(video, false).FireAndForget(Logger);
    public void PlayAlternate(VideoSummary video) => PlayInternalAsync(video, true).FireAndForget(Logger);
    public void AddToQueue(VideoSummary video) { _queue.Add(video); _notifications.ShowToast($"Added “{video.Title}” to queue"); }

    public void OpenChannel(VideoSummary video)
    {
        if (!string.IsNullOrWhiteSpace(video.ChannelUrl) && !video.ChannelUrl.Contains("UC0000000000000000000000", StringComparison.OrdinalIgnoreCase))
            _navigation.NavigateToChannel(video.ChannelUrl, video.ChannelName);
    }

    private static void CopyLink(VideoSummary video)
    {
        var link = string.IsNullOrWhiteSpace(video.WatchUrl) ? PlaybackRequest.BuildWatchUrl(video.Id) : video.WatchUrl;
        if (!string.IsNullOrWhiteSpace(link)) Display.GetDefault()?.GetClipboard()?.SetText(link);
    }

    private async Task PlayInternalAsync(VideoSummary video, bool alternate)
    {
        try
        {
            var task = alternate ? _playback.PlayAlternateAsync(new PlaybackRequest([video])) : _playback.PlayAsync(new PlaybackRequest([video]));
            var result = await task.ConfigureAwait(false);
            if (!PlaybackResult.IsSuccessStatus(result)) _notifications.ShowToast(result);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to start playback for video");
            _notifications.ShowToast($"Could not start playback for “{video.Title}”.");
        }
    }

    private static SimpleAction CreateAction(string name, Action handler) { var a = SimpleAction.New(name, null); a.OnActivate += (_, _) => handler(); return a; }

    public void Dispose()
    {
        if (_popoverMenu is not null) { _popoverMenu.Popdown(); _popoverMenu.Unparent(); _popoverMenu.InsertActionGroup("video", null); _popoverMenu.Dispose(); _popoverMenu = null; }
        _parentWidget?.InsertActionGroup("video", null);
        foreach (var action in _actions) action.Dispose();
        _actionGroup.Dispose();
    }
}
