using System;
using System.Collections.Generic;

namespace SilverScreen.Shell;

/// <summary>
/// Identifies the navigable destinations within the application shell.
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
/// Provides event data for navigation page transition events.
/// </summary>
public sealed class NavigationPageChangedEventArgs : EventArgs
{
    public NavigationPage? PreviousPage { get; }
    public NavigationPage CurrentPage { get; }

    public NavigationPageChangedEventArgs(NavigationPage? previousPage, NavigationPage currentPage)
    {
        PreviousPage = previousPage;
        CurrentPage = currentPage;
    }
}

/// <summary>
/// Registration descriptor for a navigation page destination.
/// </summary>
public sealed class NavigationPageRegistration
{
    public NavigationPage Page { get; }
    public string StackName { get; }
    public bool IsShellPage { get; }
    public Action? OnEnter { get; }
    public Action? OnLeave { get; }

    public NavigationPageRegistration(
        NavigationPage page,
        string stackName,
        bool isShellPage = true,
        Action? onEnter = null,
        Action? onLeave = null)
    {
        Page = page;
        StackName = stackName;
        IsShellPage = isShellPage;
        OnEnter = onEnter;
        OnLeave = onLeave;
    }
}

/// <summary>
/// Defines the contract for high-level shell navigation.
/// </summary>
public interface INavigationService
{
    NavigationPage CurrentPage { get; }
    NavigationPage? PreviousPage { get; }
    event EventHandler<NavigationPageChangedEventArgs>? PageChanged;
    bool NavigateTo(NavigationPage page);
    bool CanNavigateTo(NavigationPage page);
    void SyncFromViewStack(string? childName);
}

/// <summary>
/// Coordinates typed page navigation across main and view stacks.
/// </summary>
public sealed class NavigationService : INavigationService, IDisposable
{
    private const string ShellStackName = "shell";

    private readonly Gtk.Stack _mainStack;
    private readonly Adw.ViewStack _viewStack;
    private readonly Dictionary<NavigationPage, NavigationPageRegistration> _registry = new();
    private readonly Dictionary<string, NavigationPage> _viewStackNameToPage = new(StringComparer.Ordinal);
    private NavigationPage _currentPage = NavigationPage.Home;
    private NavigationPage? _previousPage;
    private bool _isNavigating;
    private bool _disposed;

    public NavigationPage CurrentPage => _currentPage;
    public NavigationPage? PreviousPage => _previousPage;

    public event EventHandler<NavigationPageChangedEventArgs>? PageChanged;

    public NavigationService(Gtk.Stack mainStack, Adw.ViewStack viewStack)
    {
        _mainStack = mainStack ?? throw new ArgumentNullException(nameof(mainStack));
        _viewStack = viewStack ?? throw new ArgumentNullException(nameof(viewStack));

        RegisterPage(NavigationPage.Home, "home", isShellPage: true);
        RegisterPage(NavigationPage.Subscriptions, "subscriptions", isShellPage: true);
        RegisterPage(NavigationPage.History, "history", isShellPage: true);
        RegisterPage(NavigationPage.Search, "search", isShellPage: true);
        RegisterPage(NavigationPage.Channel, "channel", isShellPage: true);
        RegisterPage(NavigationPage.Player, "player", isShellPage: false);
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
        if (isShellPage)
        {
            _viewStackNameToPage[stackName] = page;
        }
    }

    public void Initialize(NavigationPage initialPage = NavigationPage.Home)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _currentPage = initialPage;
        if (_registry.TryGetValue(initialPage, out var entry))
        {
            if (entry.IsShellPage)
            {
                _mainStack.VisibleChildName = ShellStackName;
                _viewStack.VisibleChildName = entry.StackName;
            }
            else
            {
                _mainStack.VisibleChildName = entry.StackName;
            }
        }
    }

    public bool CanNavigateTo(NavigationPage page)
    {
        return !_disposed && _registry.ContainsKey(page);
    }

    public bool NavigateTo(NavigationPage page)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_registry.TryGetValue(page, out var entry))
            throw new ArgumentException($"Page '{page}' is not registered in the navigation service.", nameof(page));

        if (_currentPage == page)
            return false;

        var previous = _currentPage;
        _isNavigating = true;
        try
        {
            if (_registry.TryGetValue(previous, out var prevEntry))
                prevEntry.OnLeave?.Invoke();

            if (entry.IsShellPage)
            {
                if (!string.Equals(_mainStack.VisibleChildName, ShellStackName, StringComparison.Ordinal))
                    _mainStack.VisibleChildName = ShellStackName;

                if (!string.Equals(_viewStack.VisibleChildName, entry.StackName, StringComparison.Ordinal))
                    _viewStack.VisibleChildName = entry.StackName;
            }
            else
            {
                if (!string.Equals(_mainStack.VisibleChildName, entry.StackName, StringComparison.Ordinal))
                    _mainStack.VisibleChildName = entry.StackName;
            }

            _previousPage = previous;
            _currentPage = page;
            entry.OnEnter?.Invoke();
        }
        finally
        {
            _isNavigating = false;
        }

        PageChanged?.Invoke(this, new NavigationPageChangedEventArgs(previous, page));
        return true;
    }

    public void SyncFromViewStack(string? childName)
    {
        if (_disposed || _isNavigating || string.IsNullOrEmpty(childName))
            return;

        if (!_viewStackNameToPage.TryGetValue(childName, out var page))
            return;

        if (_currentPage == page)
            return;

        var previous = _currentPage;
        if (_registry.TryGetValue(previous, out var prevEntry))
            prevEntry.OnLeave?.Invoke();

        _previousPage = previous;
        _currentPage = page;

        if (_registry.TryGetValue(page, out var entry))
            entry.OnEnter?.Invoke();

        PageChanged?.Invoke(this, new NavigationPageChangedEventArgs(previous, page));
    }

    public bool TryGetPage(string stackName, out NavigationPage page)
    {
        return _viewStackNameToPage.TryGetValue(stackName, out page);
    }

    public string? GetStackName(NavigationPage page)
    {
        return _registry.TryGetValue(page, out var entry) ? entry.StackName : null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _registry.Clear();
        _viewStackNameToPage.Clear();
    }
}
