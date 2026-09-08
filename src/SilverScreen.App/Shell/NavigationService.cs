using Adw;
using Gtk;

namespace SilverScreen.Shell;

/// <summary>
///     Identifies the navigable destinations within the application shell.
/// </summary>
public enum NavigationPage
{
    Home,
    Subscriptions,
    History,
    Search,
    Channel,
    Player
}

/// <summary>
///     Represents a navigation history entry with a target page and optional parameter payload.
///     Player entries are transient: they preserve the return route while open, but are never
///     added as a destination when navigating elsewhere.
/// </summary>
public sealed record NavigationEntry(NavigationPage Page, object? Parameter = null);

/// <summary>
///     Provides event data for navigation page transition events.
/// </summary>
public sealed class NavigationPageChangedEventArgs(
    NavigationEntry? previousEntry,
    NavigationEntry currentEntry,
    bool isBackNavigation = false)
    : EventArgs
{
    public NavigationPageChangedEventArgs(
        NavigationPage? previousPage,
        NavigationPage currentPage)
        : this(
            previousPage.HasValue ? new NavigationEntry(previousPage.Value) : null,
            new NavigationEntry(currentPage))
    {
    }

    public NavigationPageChangedEventArgs(
        NavigationPage? previousPage,
        NavigationPage currentPage,
        object? previousParameter,
        object? currentParameter,
        bool isBackNavigation = false)
        : this(
            previousPage.HasValue ? new NavigationEntry(previousPage.Value, previousParameter) : null,
            new NavigationEntry(currentPage, currentParameter),
            isBackNavigation)
    {
    }

    public NavigationPage? PreviousPage { get; } = previousEntry?.Page;
    public NavigationPage CurrentPage { get; } = currentEntry.Page;
    public object? PreviousParameter { get; } = previousEntry?.Parameter;
    public object? CurrentParameter { get; } = currentEntry.Parameter;
    public NavigationEntry? PreviousEntry { get; } = previousEntry;
    public NavigationEntry CurrentEntry { get; } = currentEntry;
    public bool IsBackNavigation { get; } = isBackNavigation;
}

/// <summary>
///     Registration descriptor for a navigation page destination.
/// </summary>
public sealed class NavigationPageRegistration(
    NavigationPage page,
    string stackName,
    bool isShellPage = true,
    Action? onEnter = null,
    Action? onLeave = null)
{
    public NavigationPage Page { get; } = page;
    public string StackName { get; } = stackName;
    public bool IsShellPage { get; } = isShellPage;
    public Action? OnEnter { get; } = onEnter;
    public Action? OnLeave { get; } = onLeave;
}

/// <summary>
///     Defines the contract for high-level shell navigation.
/// </summary>
public interface INavigationService
{
    NavigationPage CurrentPage { get; }
    object? CurrentParameter { get; }
    NavigationEntry CurrentEntry { get; }
    NavigationPage? PreviousPage { get; }
    object? PreviousParameter { get; }
    NavigationEntry? PreviousEntry { get; }
    bool CanGoBack { get; }
    event EventHandler<NavigationPageChangedEventArgs>? PageChanged;
    bool NavigateTo(NavigationPage page);
    bool NavigateTo(NavigationPage page, object? parameter);
    bool GoBack();
    bool CanNavigateTo(NavigationPage page);
    void SyncFromViewStack(string? childName);
}

/// <summary>
///     Coordinates typed page navigation across main and view stacks.
/// </summary>
public sealed class NavigationService : INavigationService, IDisposable
{
    private const string ShellStackName = "shell";
    private readonly Stack<NavigationEntry> _backStack = new();

    private readonly Stack? _mainStack;
    private readonly Dictionary<NavigationPage, NavigationPageRegistration> _registry = new();
    private readonly ViewStack? _viewStack;
    private readonly Dictionary<string, NavigationPage> _viewStackNameToPage = new(StringComparer.Ordinal);
    private bool _disposed;
    private bool _isNavigating;

    public NavigationService(Stack? mainStack = null, ViewStack? viewStack = null)
    {
        _mainStack = mainStack;
        _viewStack = viewStack;

        RegisterPage(NavigationPage.Home, "home");
        RegisterPage(NavigationPage.Subscriptions, "subscriptions");
        RegisterPage(NavigationPage.History, "history");
        RegisterPage(NavigationPage.Search, "search");
        RegisterPage(NavigationPage.Channel, "channel");
        RegisterPage(NavigationPage.Player, "player", false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _backStack.Clear();
        _registry.Clear();
        _viewStackNameToPage.Clear();
    }

    public NavigationPage CurrentPage => CurrentEntry.Page;
    public object? CurrentParameter => CurrentEntry.Parameter;
    public NavigationEntry CurrentEntry { get; private set; } = new(NavigationPage.Home);

    public NavigationPage? PreviousPage => _backStack.Count > 0 ? _backStack.Peek().Page : null;
    public object? PreviousParameter => _backStack.Count > 0 ? _backStack.Peek().Parameter : null;
    public NavigationEntry? PreviousEntry => _backStack.Count > 0 ? _backStack.Peek() : null;
    public bool CanGoBack => _backStack.Count > 0;

    public event EventHandler<NavigationPageChangedEventArgs>? PageChanged;

    public bool CanNavigateTo(NavigationPage page)
    {
        return !_disposed && _registry.ContainsKey(page);
    }

    public bool NavigateTo(NavigationPage page)
    {
        return NavigateTo(page, null);
    }

    public bool NavigateTo(NavigationPage page, object? parameter)
    {
        return NavigateInternal(new NavigationEntry(page, parameter), true, false);
    }

    public bool GoBack()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        while (_backStack.Count > 0)
        {
            var target = _backStack.Pop();
            if (target.Page == NavigationPage.Player)
                continue;

            return NavigateInternal(target, false, true);
        }

        return false;
    }

    public void SyncFromViewStack(string? childName)
    {
        if (_disposed || _isNavigating || string.IsNullOrEmpty(childName))
            return;

        if (!_viewStackNameToPage.TryGetValue(childName, out var page))
            return;

        if (CurrentEntry.Page == page && CurrentEntry.Parameter is null)
            return;

        var previous = CurrentEntry;
        if (_registry.TryGetValue(previous.Page, out var prevEntry))
            prevEntry.OnLeave?.Invoke();

        // Selecting a shell tab establishes a new navigation root. Tab changes and
        // detail/search navigation must not share the same back history.
        _backStack.Clear();
        CurrentEntry = new NavigationEntry(page);

        if (_registry.TryGetValue(page, out var entry))
            entry.OnEnter?.Invoke();

        PageChanged?.Invoke(this, new NavigationPageChangedEventArgs(previous, CurrentEntry));
    }

    public void RegisterPage(
        NavigationPage page,
        string stackName,
        bool isShellPage = true,
        Action? onEnter = null,
        Action? onLeave = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var registration = new NavigationPageRegistration(page, stackName, isShellPage, onEnter, onLeave);
        _registry[page] = registration;
        if (isShellPage) _viewStackNameToPage[stackName] = page;
    }

    public void Initialize(NavigationPage initialPage = NavigationPage.Home, object? parameter = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _backStack.Clear();
        CurrentEntry = new NavigationEntry(initialPage, parameter);
        if (!_registry.TryGetValue(initialPage, out var entry)) return;
        if (entry.IsShellPage)
        {
            if (_mainStack is not null &&
                !string.Equals(_mainStack.VisibleChildName, ShellStackName, StringComparison.Ordinal))
                _mainStack.VisibleChildName = ShellStackName;

            if (_viewStack is not null &&
                !string.Equals(_viewStack.VisibleChildName, entry.StackName, StringComparison.Ordinal))
                _viewStack.VisibleChildName = entry.StackName;
        }
        else
        {
            if (_mainStack is not null &&
                !string.Equals(_mainStack.VisibleChildName, entry.StackName, StringComparison.Ordinal))
                _mainStack.VisibleChildName = entry.StackName;
        }
    }


    private bool NavigateInternal(NavigationEntry target, bool pushToBackStack, bool isBackNavigation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_registry.TryGetValue(target.Page, out var entry))
            throw new ArgumentException($"Page '{target.Page}' is not registered in the navigation service.",
                nameof(target));

        if (CurrentEntry == target ||
            (CurrentEntry.Page == target.Page && Equals(CurrentEntry.Parameter, target.Parameter)))
            return false;

        var previous = CurrentEntry;
        _isNavigating = true;
        try
        {
            if (_registry.TryGetValue(previous.Page, out var prevEntry))
                prevEntry.OnLeave?.Invoke();

            if (entry.IsShellPage)
            {
                if (_mainStack is not null &&
                    !string.Equals(_mainStack.VisibleChildName, ShellStackName, StringComparison.Ordinal))
                    _mainStack.VisibleChildName = ShellStackName;

                if (_viewStack is not null &&
                    !string.Equals(_viewStack.VisibleChildName, entry.StackName, StringComparison.Ordinal))
                    _viewStack.VisibleChildName = entry.StackName;
            }
            else
            {
                if (_mainStack is not null &&
                    !string.Equals(_mainStack.VisibleChildName, entry.StackName, StringComparison.Ordinal))
                    _mainStack.VisibleChildName = entry.StackName;
            }
            if (pushToBackStack)
            {
                if (IsShellRoot(target.Page))
                    _backStack.Clear();
                else if (previous.Page != NavigationPage.Player)
                    _backStack.Push(previous);
            }
            CurrentEntry = target;
            entry.OnEnter?.Invoke();
        }
        finally
        {
            _isNavigating = false;
        }

        PageChanged?.Invoke(this, new NavigationPageChangedEventArgs(previous, target, isBackNavigation));
        return true;
    }

    private static bool IsShellRoot(NavigationPage page)
    {
        return page is NavigationPage.Home or NavigationPage.Subscriptions or NavigationPage.History;
    }
    public bool TryGetPage(string stackName, out NavigationPage page)
    {
        return _viewStackNameToPage.TryGetValue(stackName, out page);
    }

    public string? GetStackName(NavigationPage page)
    {
        return _registry.TryGetValue(page, out var entry) ? entry.StackName : null;
    }
}