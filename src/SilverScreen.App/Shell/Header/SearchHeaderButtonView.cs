using Gtk;
using SilverScreen.Browsing.Search;
using XSTH.Blueprint.Helpers;
using Object = GObject.Object;

namespace SilverScreen.Shell.Header;

/// <summary>
///     Encapsulates the search button and popover in the shell header bar.
/// </summary>
public partial class SearchHeaderButtonView : ViewBase<MenuButton>
{
    private readonly SearchPopoverView _searchPopover;

    public SearchHeaderButtonView(
        SearchViewModel searchViewModel,
        INavigationService navigationService,
        INotificationService notificationService)
    {
        ArgumentNullException.ThrowIfNull(searchViewModel);
        ArgumentNullException.ThrowIfNull(navigationService);
        ArgumentNullException.ThrowIfNull(notificationService);

        _searchPopover = Lifetime.Own(new SearchPopoverView(
            searchViewModel,
            navigationService,
            notificationService,
            search_popover.Popdown));
        search_popover.Child = _searchPopover.Widget;

        Lifetime.Track(() => search_popover.OnClosed += OnClosed, () => search_popover.OnClosed -= OnClosed);
        Lifetime.Track(() => search_popover.OnNotify += OnNotify, () => search_popover.OnNotify -= OnNotify);
    }

    private void OnClosed(Popover sender, EventArgs args)
    {
        _searchPopover.OnClosed();
    }

    private void OnNotify(Object sender, Object.NotifySignalArgs args)
    {
        if (args.Pspec.GetName() == "visible" && search_popover.GetVisible())
            _searchPopover.OnOpened();
    }
}