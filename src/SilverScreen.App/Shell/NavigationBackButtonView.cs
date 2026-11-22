using Gtk;
using SilverScreen.Browsing.Channel;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Shell;

/// <summary>
///     Encapsulates the shell header back navigation button with tooltip tracking.
/// </summary>
public partial class NavigationBackButtonView : ViewBase<Button>
{
    private readonly INavigationService _navigationService;

    public NavigationBackButtonView(INavigationService navigationService)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));

        Lifetime.Track(
            () => _navigationService.PageChanged += OnPageChanged,
            () => _navigationService.PageChanged -= OnPageChanged);

        UpdateState();
    }

    private void OnClicked(Button sender, EventArgs args)
    {
        if (!_navigationService.GoBack())
            _navigationService.NavigateTo(NavigationPage.Home);
    }

    private void OnPageChanged(object? sender, NavigationPageChangedEventArgs e)
    {
        Lifetime.Idle(() =>
        {
            if (!IsDisposed)
                UpdateState();
            return false;
        });
    }

    private void UpdateState()
    {
        var visible = _navigationService.CurrentPage is NavigationPage.Search or NavigationPage.Channel &&
                      _navigationService.CanGoBack;
        Widget.Visible = visible;
        Widget.TooltipText = visible ? GetBackButtonTooltip() : "Back";
    }

    private string GetBackButtonTooltip()
    {
        var previous = _navigationService.PreviousEntry;
        if (previous is null)
            return "Back";

        var destination = previous.Page switch
        {
            NavigationPage.Home => "Home",
            NavigationPage.Subscriptions => "Subscriptions",
            NavigationPage.History => "History",
            NavigationPage.Search when previous.Parameter is string query &&
                                       !string.IsNullOrWhiteSpace(query) => $"Search results for “{query}”",
            NavigationPage.Search => "Search",
            NavigationPage.Channel when previous.Parameter is ChannelNavigationArgs channel &&
                                        !string.IsNullOrWhiteSpace(channel.Name) => channel.Name,
            NavigationPage.Channel => "Channel",
            _ => "the previous page"
        };

        return $"Back to {destination}";
    }
}
