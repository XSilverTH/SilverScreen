using Gdk;
using Gio;
using GObject;
using Gtk;
using SilverScreen.Core.Browsing.Subscriptions;

namespace SilverScreen.Browsing.Subscriptions;

public sealed class SubscriptionsChannelContextMenu(INavigationService navigation) : IDisposable
{
    private readonly INavigationService _navigation =
        navigation ?? throw new ArgumentNullException(nameof(navigation));

    private SimpleActionGroup? _actionGroup;
    private SubscribedChannel? _activeChannel;
    private bool _disposed;
    private Menu? _menuModel;
    private SimpleAction? _openAction;
    private SignalHandler<SimpleAction, SimpleAction.ActivateSignalArgs>? _openActivateHandler;
    private Widget? _parentWidget;
    private PopoverMenu? _popoverMenu;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_popoverMenu is not null)
        {
            _popoverMenu.Popdown();
            _popoverMenu.Unparent();
            _popoverMenu.InsertActionGroup("channel-item", null);
            _popoverMenu.Dispose();
            _popoverMenu = null;
        }

        _parentWidget = null;
        _activeChannel = null;

        if (_openAction is not null)
        {
            if (_openActivateHandler is not null)
            {
                _openAction.OnActivate -= _openActivateHandler;
                _openActivateHandler = null;
            }

            _openAction.Dispose();
            _openAction = null;
        }

        _actionGroup?.Dispose();
        _actionGroup = null;
        _menuModel?.Dispose();
        _menuModel = null;
    }

    public void ShowFor(Widget widget, SubscribedChannel channel, double x, double y)
    {
        if (_disposed) return;
        _activeChannel = channel;
        EnsurePopover();

        if (_popoverMenu is null) return;

        if (!ReferenceEquals(_parentWidget, widget))
        {
            _popoverMenu.Popdown();
            if (_parentWidget is not null)
                _popoverMenu.Unparent();
            _popoverMenu.SetParent(widget);
            _parentWidget = widget;
        }

        _popoverMenu.SetPointingTo(new Rectangle { X = (int)x, Y = (int)y, Width = 1, Height = 1 });
        _popoverMenu.Popup();
    }

    public void DetachIfAttached(Widget widget)
    {
        if (_popoverMenu is null || !ReferenceEquals(_parentWidget, widget)) return;
        _popoverMenu.Popdown();
        _popoverMenu.Unparent();
        _parentWidget = null;
        _activeChannel = null;
    }

    private void EnsurePopover()
    {
        if (_popoverMenu is not null) return;

        _menuModel = Menu.New();
        _menuModel.Append("Go to Channel page", "channel-item.open-channel");

        _actionGroup = SimpleActionGroup.New();
        _openAction = SimpleAction.New("open-channel", null);
        _openActivateHandler = (_, _) =>
        {
            if (_activeChannel is { } channel)
                _navigation.NavigateToChannel(channel.Url, channel.Title);
        };
        _openAction.OnActivate += _openActivateHandler;
        _actionGroup.AddAction(_openAction);

        _popoverMenu = PopoverMenu.NewFromModel(_menuModel);
        _popoverMenu.HasArrow = false;
        _popoverMenu.InsertActionGroup("channel-item", _actionGroup);
    }
}
