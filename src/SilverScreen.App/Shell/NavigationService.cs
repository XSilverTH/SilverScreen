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
/// Represents a navigation history entry with a target page and optional parameter payload.
/// </summary>
public sealed record NavigationEntry(NavigationPage Page, object? Parameter = null);

/// <summary>
/// Provides event data for navigation page transition events.
/// </summary>
public sealed class NavigationPageChangedEventArgs : EventArgs
{
    public NavigationPage? PreviousPage { get; }
    public NavigationPage CurrentPage { get; }
    public object? PreviousParameter { get; }
    public object? CurrentParameter { get; }
    public NavigationEntry? PreviousEntry { get; }
    public NavigationEntry CurrentEntry { get; }
    public bool IsBackNavigation { get; }

    public NavigationPageChangedEventArgs(
        NavigationPage? previousPage,
        NavigationPage currentPage)
        : this(
            previousPage.HasValue ? new NavigationEntry(previousPage.Value) : null,
            new NavigationEntry(currentPage),
            false)
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

    public NavigationPageChangedEventArgs(
        NavigationEntry? previousEntry,
        NavigationEntry currentEntry,
        bool isBackNavigation = false)
    {
        PreviousEntry = previousEntry;
        CurrentEntry = currentEntry;
        PreviousPage = previousEntry?.Page;
        CurrentPage = currentEntry.Page;
        PreviousParameter = previousEntry?.Parameter;
        CurrentParameter = currentEntry.Parameter;
        IsBackNavigation = isBackNavigation;
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
/// Coordinates typed page navigation across main and view stacks.
/// </summary>
public sealed class NavigationService : INavigationService, IDisposable
{
    private const string ShellStackName = "shell";

    private readonly Gtk.Stack? _mainStack;
    private readonly Adw.ViewStack? _viewStack;
    private readonly Dictionary<NavigationPage, NavigationPageRegistration> _registry = new();
    private readonly Dictionary<string, NavigationPage> _viewStackNameToPage = new(StringComparer.Ordinal);
    private NavigationEntry _currentEntry = new(NavigationPage.Home);
    private readonly Stack<NavigationEntry> _backStack = new();
    private bool _isNavigating;
    private bool _disposed;

    public NavigationPage CurrentPage => _currentEntry.Page;
    public object? CurrentParameter => _currentEntry.Parameter;
    public NavigationEntry CurrentEntry => _currentEntry;
    public NavigationPage? PreviousPage => _backStack.Count > 0 ? _backStack.Peek().Page : null;
    public object? PreviousParameter => _backStack.Count > 0 ? _backStack.Peek().Parameter : null;
    public NavigationEntry? PreviousEntry => _backStack.Count > 0 ? _backStack.Peek() : null;
    public bool CanGoBack => _backStack.Count > 0;

    public event EventHandler<NavigationPageChangedEventArgs>? PageChanged;

    public NavigationService(Gtk.Stack? mainStack = null, Adw.ViewStack? viewStack = null)
    {
        _mainStack = mainStack;
        _viewStack = viewStack;

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

    public void Initialize(NavigationPage initialPage = NavigationPage.Home, object? parameter = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _currentEntry = new NavigationEntry(initialPage, parameter);
        if (_registry.TryGetValue(initialPage, out var entry))
        {
            if (entry.IsShellPage)
            {
                if (_mainStack is not null && !string.Equals(_mainStack.VisibleChildName, ShellStackName, StringComparison.Ordinal))
                    _mainStack.VisibleChildName = ShellStackName;

                if (_viewStack is not null && !string.Equals(_viewStack.VisibleChildName, entry.StackName, StringComparison.Ordinal))
                    _viewStack.VisibleChildName = entry.StackName;
            }
            else
            {
                if (_mainStack is not null && !string.Equals(_mainStack.VisibleChildName, entry.StackName, StringComparison.Ordinal))
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
        return NavigateTo(page, null);
    }

    public bool NavigateTo(NavigationPage page, object? parameter)
    {
        return NavigateInternal(new NavigationEntry(page, parameter), pushToBackStack: true, isBackNavigation: false);
    }

    public bool GoBack()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_backStack.Count == 0)
            return false;

        var target = _backStack.Pop();
        return NavigateInternal(target, pushToBackStack: false, isBackNavigation: true);
    }

    private bool NavigateInternal(NavigationEntry target, bool pushToBackStack, bool isBackNavigation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_registry.TryGetValue(target.Page, out var entry))
            throw new ArgumentException($"Page '{target.Page}' is not registered in the navigation service.", nameof(target));

        if (_currentEntry == target || (_currentEntry.Page == target.Page && Equals(_currentEntry.Parameter, target.Parameter)))
            return false;

        var previous = _currentEntry;
        _isNavigating = true;
        try
        {
            if (_registry.TryGetValue(previous.Page, out var prevEntry))
                prevEntry.OnLeave?.Invoke();

            if (entry.IsShellPage)
            {
                if (_mainStack is not null && !string.Equals(_mainStack.VisibleChildName, ShellStackName, StringComparison.Ordinal))
                    _mainStack.VisibleChildName = ShellStackName;

                if (_viewStack is not null && !string.Equals(_viewStack.VisibleChildName, entry.StackName, StringComparison.Ordinal))
                    _viewStack.VisibleChildName = entry.StackName;
            }
            else
            {
                if (_mainStack is not null && !string.Equals(_mainStack.VisibleChildName, entry.StackName, StringComparison.Ordinal))
                    _mainStack.VisibleChildName = entry.StackName;
            }

            if (pushToBackStack)
            {
                _backStack.Push(previous);
            }
            _currentEntry = target;
            entry.OnEnter?.Invoke();
        }
        finally
        {
            _isNavigating = false;
        }

        PageChanged?.Invoke(this, new NavigationPageChangedEventArgs(previous, target, isBackNavigation));
        return true;
    }

    public void SyncFromViewStack(string? childName)
    {
        if (_disposed || _isNavigating || string.IsNullOrEmpty(childName))
            return;

        if (!_viewStackNameToPage.TryGetValue(childName, out var page))
            return;

        if (_currentEntry.Page == page && _currentEntry.Parameter is null)
            return;

        var previous = _currentEntry;
        if (_registry.TryGetValue(previous.Page, out var prevEntry))
            prevEntry.OnLeave?.Invoke();

        _backStack.Push(previous);
        _currentEntry = new NavigationEntry(page);

        if (_registry.TryGetValue(page, out var entry))
            entry.OnEnter?.Invoke();

        PageChanged?.Invoke(this, new NavigationPageChangedEventArgs(previous, _currentEntry, isBackNavigation: false));
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
        _backStack.Clear();
        _registry.Clear();
        _viewStackNameToPage.Clear();
    }
}
