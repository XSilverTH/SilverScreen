namespace SilverScreen.Shell;

/// <summary>
///     Contract for navigating between application pages, decoupling UI views from concrete shell stacks.
/// </summary>
public interface INavigationService
{
    /// <summary>Gets the currently active page.</summary>
    NavigationPage CurrentPage { get; }

    /// <summary>Gets the active navigation entry, containing page and optional parameter payload.</summary>
    NavigationEntry CurrentEntry { get; }

    /// <summary>Gets the immediate previous navigation entry from the back stack, if available.</summary>
    NavigationEntry? PreviousEntry => null;

    /// <summary>Gets a value indicating whether there are pages in the back stack to return to.</summary>
    bool CanGoBack { get; }

    /// <summary>Navigates to the specified page with an optional parameter payload.</summary>
    bool NavigateTo(NavigationPage page, object? parameter = null);

    /// <summary>Navigates to the channel view with the specified channel URL and optional name.</summary>
    bool NavigateToChannel(string channelUrl, string? channelName = null);

    /// <summary>Navigates to the search view with the specified search query.</summary>
    bool NavigateToSearch(string query);

    /// <summary>Navigates to the video player view.</summary>
    bool NavigateToPlayer();

    /// <summary>Navigates back to the previous entry in the back stack.</summary>
    bool GoBack();

    /// <summary>Fired when the navigation page or parameter changes.</summary>
    event EventHandler<NavigationPageChangedEventArgs>? PageChanged;
}
