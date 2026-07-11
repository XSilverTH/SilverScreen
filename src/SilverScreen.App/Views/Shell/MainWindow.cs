using System.ComponentModel;
using Gtk;
using SilverScreen.Core.Models;
using SilverScreen.ViewModels;
using SilverScreen.Views.Components;
using SilverScreen.Views.Home;
using SilverScreen.Views.Popovers;
using SilverScreen.Views.Search;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Views.Shell;

public partial class MainWindow : WindowBase<Adw.ApplicationWindow>
{
    private readonly ApplicationServices _services;
    private readonly Action _disposeApplicationServices;
    private readonly ShellViewModel _shell = new();
    private readonly HomeView _home;
    private readonly SearchView _search;
    private readonly QueuePopoverView _queuePopover;
    private readonly AccountPopoverView _accountPopover;
    private readonly QueueViewModel _queueViewModel;
    private readonly Label _statusLabel;
    private readonly Adw.ViewStack _stack;
    private readonly Adw.ViewSwitcher _switcher;
    private readonly Button _searchButton;
    private readonly MenuButton _accountButton;
    private readonly MenuButton _appMenuButton;
    private readonly MenuButton _queueButton;
    private readonly Entry _searchEntry;
    private readonly Popover _searchPopover;
    private bool _closed;

    public MainWindow(ApplicationServices services, Action disposeApplicationServices)
    {
        _services = services;
        _disposeApplicationServices = disposeApplicationServices;
        _stack = GetRequiredObject<Adw.ViewStack>("view_stack");
        _switcher = GetRequiredObject<Adw.ViewSwitcher>("view_switcher");
        _searchButton = GetRequiredObject<Button>("search_button");
        _accountButton = GetRequiredObject<MenuButton>("account_button");
        _appMenuButton = GetRequiredObject<MenuButton>("app_menu_button");
        _queueButton = GetRequiredObject<MenuButton>("queue_button");
        _statusLabel = GetRequiredObject<Label>("status_label");

        var actions = CreateVideoActions();
        _home = new HomeView(new HomeViewModel(services.HomeFeed), services.Thumbnails, actions);
        _search = new SearchView(new SearchViewModel(services.Search, services.Playback, _shell), services.Thumbnails, actions);
        _queueViewModel = new QueueViewModel(services.Queue);
        _queuePopover = new QueuePopoverView(_queueViewModel);
        _accountPopover = new AccountPopoverView(new AccountViewModel(services.Session, services.SessionValidation, _shell),
            UpdateAccountAppearance);

        _switcher.Stack = _stack;
        _stack.AddTitled(_home.Widget, "home", "Home");
        _stack.AddTitled(_search.Widget, "search", "Search");
        _stack.AddTitled(Placeholder("Subscriptions", "Subscription feeds will land after account/session support."), "subscriptions", "Subscriptions");
        _stack.AddTitled(Placeholder("History", "Local watch history is intentionally not persisted in this shell step."), "history", "History");
        _stack.VisibleChildName = _shell.SelectedPage;

        _queueButton.Popover = CreateQueuePopover();
        _appMenuButton.Popover = CreateApplicationMenu();
        _searchPopover = CreateSearchPopover(out _searchEntry);
        _shell.PropertyChanged += OnShellPropertyChanged;
        _queueViewModel.StateChanged += OnQueueStateChanged;
        UpdateQueueButton(_queueViewModel.State);
        Widget.OnCloseRequest += OnCloseRequest;
    }

    private VideoCardActions CreateVideoActions() => new()
    {
        PlayAsync = async video => _shell.Status = await _services.Playback.PlayAsync(new PlaybackRequest(video)).ConfigureAwait(false),
        AddToQueue = video =>
        {
            _services.Queue.Add(video);
            _shell.Status = "Video added to queue.";
        },
        AddNext = video =>
        {
            _services.Queue.AddNext(video);
            _shell.Status = "Video added next in queue.";
        },
        ReportStatus = message => _shell.Status = message
    };

    private void OnSearchButtonClicked(object? sender, EventArgs args)
    {
        _searchPopover.Popup();
        _searchEntry.GrabFocus();
    }

    private Popover CreateSearchPopover(out Entry entry)
    {
        entry = Entry.New();
        entry.PlaceholderText = "Search or paste a YouTube URL";
        entry.WidthChars = 36;
        var searchEntry = entry;
        entry.OnActivate += async (_, _) =>
        {
            await _search.SubmitAsync(searchEntry.GetText());
            _searchPopover.Popdown();
        };
        var content = Box.New(Orientation.Vertical, 10);
        content.MarginTop = 12;
        content.MarginBottom = 12;
        content.MarginStart = 12;
        content.MarginEnd = 12;
        content.Append(entry);
        var hint = Label.New("Enter searches YouTube with yt-dlp or opens a supported YouTube URL.");
        hint.Xalign = 0;
        hint.Wrap = true;
        hint.CssClasses = ["dim-label"];
        content.Append(hint);
        var popover = Popover.New();
        popover.Child = content;
        popover.SetParent(_searchButton);
        return popover;
    }

    private Popover CreateQueuePopover()
    {
        var popover = Popover.New();
        popover.Child = _queuePopover.Widget;
        return popover;
    }

    private Popover CreateApplicationMenu()
    {
        var content = Box.New(Orientation.Vertical, 4);
        content.MarginTop = 6;
        content.MarginBottom = 6;
        content.MarginStart = 6;
        content.MarginEnd = 6;
        content.Append(MenuAction("Preferences", () => _shell.Status = "Preferences stub"));
        content.Append(MenuAction("About SilverScreen", () => _shell.Status = "About stub: SilverScreen"));
        content.Append(MenuAction("Quit", () => Widget.Close()));
        var popover = Popover.New();
        popover.Child = content;
        return popover;
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        GLib.Functions.IdleAdd(0, () =>
        {
            if (_closed)
            {
                return false;
            }

            if (args.PropertyName == nameof(ShellViewModel.Status))
            {
                Console.WriteLine(_shell.Status);
                _statusLabel.SetText(_shell.Status);
            }
            else if (args.PropertyName == nameof(ShellViewModel.SelectedPage))
            {
                _stack.VisibleChildName = _shell.SelectedPage;
            }

            return false;
        });
    }

    private void OnQueueStateChanged(object? sender, QueuePresentationState state)
    {
        GLib.Functions.IdleAdd(0, () =>
        {
            if (!_closed)
            {
                UpdateQueueButton(state);
            }

            return false;
        });
    }

    private void UpdateQueueButton(QueuePresentationState state)
    {
        _queueButton.Visible = state.IsVisible;
        _queueButton.Child = QueueButtonContent(FormatQueuedDuration(state.TotalDuration));
    }

    private void UpdateAccountAppearance(bool hasManualSession)
    {
        _accountButton.TooltipText = hasManualSession ? "Manual YouTube session active" : "Account";
    }

    private bool OnCloseRequest(Window sender, EventArgs args)
    {
        if (_closed) return false;
        _closed = true;
        _shell.PropertyChanged -= OnShellPropertyChanged;
        _queueViewModel.StateChanged -= OnQueueStateChanged;
        _home.Dispose();
        _search.Dispose();
        _queuePopover.Dispose();
        _accountPopover.Dispose();
        _disposeApplicationServices();
        Dispose();

        return false;
    }

    private static Widget Placeholder(string title, string description)
    {
        var page = Adw.StatusPage.New();
        page.Title = title;
        page.Description = description;
        page.IconName = "applications-internet-symbolic";
        return page;
    }

    private static Box QueueButtonContent(string duration)
    {
        var content = Box.New(Orientation.Horizontal, 6);
        content.MarginStart = 10;
        content.MarginEnd = 10;
        content.MarginTop = 6;
        content.MarginBottom = 6;
        var icon = Image.NewFromIconName("playlist-symbolic");
        icon.PixelSize = 16;
        content.Append(icon);
        content.Append(Label.New(duration));
        return content;
    }

    private static Button MenuAction(string label, Action action)
    {
        var button = Button.NewWithLabel(label);
        button.Halign = Align.Fill;
        button.CssClasses = ["flat"];
        button.OnClicked += (_, _) => action();
        return button;
    }

    private static string FormatQueuedDuration(TimeSpan duration) => duration.TotalHours >= 1
        ? $"{(int)duration.TotalHours}h {duration.Minutes:00}m"
        : duration.TotalMinutes >= 1
            ? $"{(int)duration.TotalMinutes}m"
            : $"{duration.Seconds}s";
}
