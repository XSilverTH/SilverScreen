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
    private readonly INavigationService _navigationService;
    private readonly INotificationService _notificationService;
    private readonly SearchViewModel _searchViewModel;
    private SearchPopoverView? _searchPopover;

    public SearchHeaderButtonView(
        SearchViewModel searchViewModel,
        INavigationService navigationService,
        INotificationService notificationService)
    {
        _searchViewModel = searchViewModel ?? throw new ArgumentNullException(nameof(searchViewModel));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));

        Lifetime.Track(() => search_popover.OnClosed += OnClosed, () => search_popover.OnClosed -= OnClosed);
        Lifetime.Track(() => search_popover.OnNotify += OnNotify, () => search_popover.OnNotify -= OnNotify);
    }

    private SearchPopoverView EnsureSearchPopover()
    {
        if (_searchPopover is not null)
            return _searchPopover;

        var popover = Lifetime.Own(new SearchPopoverView(
            _searchViewModel,
            _navigationService,
            _notificationService,
            search_popover.Popdown));
        search_popover.Child = popover.Widget;
        _searchPopover = popover;
        return popover;
    }

    private void OnClosed(Popover sender, EventArgs args)
    {
        _searchPopover?.OnClosed();
    }

    private void OnNotify(Object sender, Object.NotifySignalArgs args)
    {
        if (args.Pspec.GetName() == "visible" && search_popover.GetVisible() && !IsDisposed)
            EnsureSearchPopover().OnOpened();
    }
}