using Gdk;
using Gio;
using GObject;
using Gtk;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Player;
using SilverScreen.Core.Queue;
using Action = System.Action;
using Task = System.Threading.Tasks.Task;

namespace SilverScreen.Browsing.Components;

public sealed class VideoCardContextMenu : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<VideoCardContextMenu>();
    private static Menu? _sharedMenuModel;
    private readonly SimpleActionGroup _actionGroup = SimpleActionGroup.New();
    private readonly (SimpleAction Action, SignalHandler<SimpleAction, SimpleAction.ActivateSignalArgs> Handler)[]
        _actions;
    private readonly INavigationService _navigation;
    private readonly INotificationService _notifications;
    private readonly IPlaybackService _playback;
    private readonly IQueueService _queue;
    private MenuButton? _menuButton;
    private Widget? _parentWidget;
    private PopoverMenu? _popoverMenu;
    private VideoSummary? _video;

    public VideoCardContextMenu(INavigationService navigation, IPlaybackService playback, IQueueService queue,
        INotificationService notifications)
    {
        _navigation = navigation;
        _playback = playback;
        _queue = queue;
        _notifications = notifications;
        _actions =
        [
            CreateAction("play", () =>
            {
                if (_video is { } v) Play(v);
            }),
            CreateAction("play-alternate", () =>
            {
                if (_video is { } v) PlayAlternate(v);
            }),
            CreateAction("add-to-queue", () =>
            {
                if (_video is { } v) AddToQueue(v);
            }),
            CreateAction("open-channel", () =>
            {
                if (_video is { } v) OpenChannel(v);
            }),
            CreateAction("copy-link", () =>
            {
                if (_video is { } v) CopyLink(v);
            })
        ];
        foreach (var (action, _) in _actions) _actionGroup.AddAction(action);
    }

    public void Dispose()
    {
        if (_popoverMenu is not null)
        {
            _popoverMenu.Popdown();
            _popoverMenu.Unparent();
            _popoverMenu.InsertActionGroup("video", null);
            _popoverMenu.Dispose();
            _popoverMenu = null;
        }

        _menuButton?.InsertActionGroup("video", null);
        _menuButton = null;
        _parentWidget?.InsertActionGroup("video", null);
        _parentWidget = null;
        foreach (var (action, handler) in _actions)
        {
            action.OnActivate -= handler;
            action.Dispose();
        }

        _actionGroup.Dispose();
    }

    public void AttachTo(Widget parentWidget, MenuButton menuButton)
    {
        _parentWidget = parentWidget;
        _menuButton = menuButton;
        menuButton.MenuModel = GetSharedMenuModel();
        menuButton.InsertActionGroup("video", _actionGroup);
        parentWidget.InsertActionGroup("video", _actionGroup);
    }

    public void SetVideo(VideoSummary? video)
    {
        _video = video;
    }

    public void Dismiss()
    {
        _popoverMenu?.Popdown();
    }

    public void ShowAt(int x = -1, int y = -1)
    {
        if (_video is null || _parentWidget is null) return;
        EnsurePopover();
        if (_popoverMenu is null) return;
        _popoverMenu.SetPointingTo(new Rectangle
        {
            X = x >= 0 ? x : Math.Max(0, _parentWidget.GetWidth() / 2),
            Y = y >= 0 ? y : Math.Max(0, _parentWidget.GetHeight() / 2), Width = 1, Height = 1
        });
        _popoverMenu.Popup();
    }

    private void EnsurePopover()
    {
        if (_popoverMenu is not null || _parentWidget is null) return;
        _popoverMenu = PopoverMenu.NewFromModel(GetSharedMenuModel());
        _popoverMenu.HasArrow = false;
        _popoverMenu.SetParent(_parentWidget);
        _popoverMenu.InsertActionGroup("video", _actionGroup);
    }

    private static Menu GetSharedMenuModel()
    {
        if (_sharedMenuModel is not null) return _sharedMenuModel;
        var menuModel = Menu.New();
        menuModel.Append("Play", "video.play");
        menuModel.Append("Open in other player", "video.play-alternate");
        menuModel.Append("Add to queue", "video.add-to-queue");
        menuModel.Append("Open channel", "video.open-channel");
        menuModel.Append("Copy link", "video.copy-link");
        _sharedMenuModel = menuModel;
        return menuModel;
    }

    public void Play(VideoSummary video)
    {
        PlayInternalAsync(video, false).FireAndForget(Logger);
    }

    private void PlayAlternate(VideoSummary video)
    {
        PlayInternalAsync(video, true).FireAndForget(Logger);
    }

    public void AddToQueue(VideoSummary video)
    {
        _queue.Add(video);
        _notifications.ShowToast($"Added “{video.Title}” to queue");
    }

    public void OpenChannel(VideoSummary video)
    {
        if (!string.IsNullOrWhiteSpace(video.ChannelUrl) &&
            !video.ChannelUrl.Contains("UC0000000000000000000000", StringComparison.OrdinalIgnoreCase))
            _navigation.NavigateToChannel(video.ChannelUrl, video.ChannelName);
    }

    private static void CopyLink(VideoSummary video)
    {
        var link = string.IsNullOrWhiteSpace(video.WatchUrl) ? PlaybackRequest.BuildWatchUrl(video.Id) : video.WatchUrl;
        if (!string.IsNullOrWhiteSpace(link)) Display.GetDefault()?.GetClipboard().SetText(link);
    }

    private async Task PlayInternalAsync(VideoSummary video, bool alternate)
    {
        try
        {
            var task = alternate
                ? _playback.PlayAlternateAsync(new PlaybackRequest([video]))
                : _playback.PlayAsync(new PlaybackRequest([video]));
            var result = await task.ConfigureAwait(false);
            if (!PlaybackResult.IsSuccessStatus(result)) _notifications.ShowToast(result);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to start playback for video");
            _notifications.ShowToast($"Could not start playback for “{video.Title}”.");
        }
    }

    private static (SimpleAction Action, SignalHandler<SimpleAction, SimpleAction.ActivateSignalArgs> Handler)
        CreateAction(string name, Action handler)
    {
        var a = SimpleAction.New(name, null);
        SignalHandler<SimpleAction, SimpleAction.ActivateSignalArgs> onActivate = (_, _) => handler();
        a.OnActivate += onActivate;
        return (a, onActivate);
    }
}
