using Gdk;
using Gio;
using Gtk;
using SilverScreen.Core.Browsing.Subscriptions;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Browsing.Subscriptions;

internal static class SubscriptionsChannelContextMenu
{
    public static void Attach(Widget widget, SubscribedChannel channel, INavigationService navigation,
        DisposeScope lifetime)
    {
        var menu = lifetime.Own(Menu.New());
        menu.Append("Go to Channel page", "channel-item.open-channel");
        var actionGroup = lifetime.Own(SimpleActionGroup.New());
        var openAction = lifetime.Own(SimpleAction.New("open-channel", null));
        lifetime.Track(
            () => openAction.OnActivate += (_, _) => navigation.NavigateToChannel(channel.Url, channel.Title),
            () => openAction.OnActivate -= (_, _) => navigation.NavigateToChannel(channel.Url, channel.Title));
        actionGroup.AddAction(openAction);

        var popover = PopoverMenu.NewFromModel(menu);
        popover.HasArrow = false;
        lifetime.Track(
            () =>
            {
                popover.SetParent(widget);
                popover.InsertActionGroup("channel-item", actionGroup);
            },
            () =>
            {
                popover.Popdown();
                popover.Unparent();
                popover.InsertActionGroup("channel-item", null);
            });

        var rightClick = GestureClick.New();
        rightClick.Button = 3;
        lifetime.Attach(widget, rightClick,
            c => c.OnPressed += (s, a) =>
            {
                s.SetState(EventSequenceState.Claimed);
                popover.SetPointingTo(new Rectangle { X = (int)a.X, Y = (int)a.Y, Width = 1, Height = 1 });
                popover.Popup();
            },
            c => c.OnPressed -= (_, _) => { });
    }
}