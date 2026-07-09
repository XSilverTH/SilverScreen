using Gtk;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Features.Queue;
using SilverScreen.Features.Playback;
using SilverScreen.Features.Search;
using SilverScreen.Infrastructure.Mock;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Views;

public partial class MainWindow : WindowBase<Adw.ApplicationWindow>
{
    private const string HomeTabName = "home";
    private const string SearchTabName = "search";
    private const string SubscriptionsTabName = "subscriptions";
    private const string HistoryTabName = "history";

    private readonly IFeedService _feedService = new MockFeedService();
    private readonly IQueueService _queueService = new QueueService();
    private readonly IPlaybackService _playbackService = new ExternalMpvPlaybackService();
    private readonly ISearchService _searchService = new YtDlpSearchService();
    private readonly ISessionService _sessionService = new MockSessionService();

    private readonly Adw.ViewStack _viewStack;
    private readonly Adw.ViewSwitcher _viewSwitcher;
    private readonly Button _searchButton;
    private readonly MenuButton _accountButton;
    private readonly MenuButton _appMenuButton;
    private readonly MenuButton _queueButton;
    private readonly Label _statusLabel;

    private readonly Popover _searchPopover;
    private readonly Entry _searchEntry;
    private readonly Label _searchSummaryLabel;
    private readonly Label _queueDurationLabel;
    private readonly Box _queueItemsBox;
    private readonly FlowBox _searchResultsFlowBox;
    private CancellationTokenSource? _searchCancellation;

    public MainWindow()
    {
        _viewStack = GetBuilderObject<Adw.ViewStack>("view_stack");
        _viewSwitcher = GetBuilderObject<Adw.ViewSwitcher>("view_switcher");
        _searchButton = GetBuilderObject<Button>("search_button");
        _accountButton = GetBuilderObject<MenuButton>("account_button");
        _appMenuButton = GetBuilderObject<MenuButton>("app_menu_button");
        _queueButton = GetBuilderObject<MenuButton>("queue_button");
        _statusLabel = GetBuilderObject<Label>("status_label");

        _searchSummaryLabel = CreateDimLabel("Search results will appear here.");
        _searchResultsFlowBox = CreateVideoFlowBox();
        _searchEntry = Entry.New();
        _searchPopover = BuildSearchPopover();
        _queueDurationLabel = Label.New(string.Empty);
        _queueItemsBox = Box.New(Orientation.Vertical, 6);

        BuildShell();
    }

    private T GetBuilderObject<T>(string id) where T : class
    {
        return Builder.GetObject(id) as T
            ?? throw new InvalidOperationException($"Blueprint object '{id}' was not found or was not a {typeof(T).Name}.");
    }

    private void BuildShell()
    {
        _viewSwitcher.Stack = _viewStack;

        BuildStaticTabs();
        BuildAccountPopover();
        BuildAppMenuPopover();
        BuildQueueButton();

        _queueService.Changed += (_, _) => RefreshQueueUi();
        _viewStack.VisibleChildName = HomeTabName;
        SetStatus("Ready");
    }

    private void BuildStaticTabs()
    {
        var homePage = CreateVideoGridPage(_feedService.GetHomeFeed().Videos.Where(video => !video.IsShort));
        _viewStack.AddTitled(homePage, HomeTabName, "Home");
        _viewStack.AddTitled(CreateSearchPage(), SearchTabName, "Search");
        _viewStack.AddTitled(CreatePlaceholderPage("Subscriptions", "Subscription feeds will land after account/session support."), SubscriptionsTabName, "Subscriptions");
        _viewStack.AddTitled(CreatePlaceholderPage("History", "Local watch history is intentionally not persisted in this shell step."), HistoryTabName, "History");
    }

    private FlowBox CreateVideoFlowBox()
    {
        var flowBox = FlowBox.New();
        flowBox.SelectionMode = SelectionMode.None;
        flowBox.ActivateOnSingleClick = false;
        flowBox.ColumnSpacing = 18;
        flowBox.RowSpacing = 22;
        flowBox.MinChildrenPerLine = 1;
        flowBox.MaxChildrenPerLine = 4;
        flowBox.MarginStart = 18;
        flowBox.MarginEnd = 18;
        flowBox.MarginTop = 18;
        flowBox.MarginBottom = 96;
        flowBox.Hexpand = true;
        flowBox.Vexpand = true;

        return flowBox;
    }

    private Widget CreateVideoGridPage(IEnumerable<VideoSummary> videos)
    {
        var flowBox = CreateVideoFlowBox();
        foreach (var video in videos)
        {
            flowBox.Append(CreateVideoCard(video));
        }

        var scrolledWindow = ScrolledWindow.New();
        scrolledWindow.Hexpand = true;
        scrolledWindow.Vexpand = true;
        scrolledWindow.Child = flowBox;

        return scrolledWindow;
    }

    private Widget CreateVideoCard(VideoSummary video)
    {
        var card = Box.New(Orientation.Vertical, 10);
        card.WidthRequest = 250;
        card.MarginStart = 2;
        card.MarginEnd = 2;
        card.MarginTop = 2;
        card.MarginBottom = 2;
        card.CssClasses = ["card"];

        var thumbnail = CreateThumbnailPlaceholder(video);
        card.Append(thumbnail);

        var metadataRow = Box.New(Orientation.Horizontal, 8);
        metadataRow.MarginStart = 12;
        metadataRow.MarginEnd = 8;
        metadataRow.MarginBottom = 12;

        var textColumn = Box.New(Orientation.Vertical, 3);
        textColumn.Hexpand = true;

        var title = Label.New(video.Title);
        title.Xalign = 0;
        title.Wrap = true;
        title.MaxWidthChars = 34;
        title.CssClasses = ["heading"];
        textColumn.Append(title);

        var channel = CreateDimLabel($"{video.ChannelName} • {FormatDuration(video.Duration)}");
        channel.Xalign = 0;
        textColumn.Append(channel);

        var playbackAvailability = CreateDimLabel(HasPlayableUrl(video) ? "Playable YouTube URL" : "Mock placeholder • no playable URL");
        playbackAvailability.Xalign = 0;
        textColumn.Append(playbackAvailability);

        metadataRow.Append(textColumn);
        metadataRow.Append(CreateVideoMenuButton(video));
        card.Append(metadataRow);

        var click = GestureClick.New();
        click.Button = 0;
        click.OnReleased += (sender, _) =>
        {
            var button = sender.GetCurrentButton();
            if (button == 1)
            {
                PlayVideo(video);
            }
            else if (button == 2)
            {
                AddToQueue(video);
            }
        };
        card.AddController(click);

        return card;
    }

    private Widget CreateThumbnailPlaceholder(VideoSummary video)
    {
        var thumbnail = Box.New(Orientation.Vertical, 6);
        thumbnail.HeightRequest = 140;
        thumbnail.MarginStart = 12;
        thumbnail.MarginEnd = 12;
        thumbnail.MarginTop = 12;
        thumbnail.Halign = Align.Fill;
        thumbnail.Valign = Align.Fill;
        thumbnail.CssClasses = ["view", "card"];

        var icon = Image.NewFromIconName("media-playback-start-symbolic");
        icon.PixelSize = 48;
        icon.Halign = Align.Center;
        icon.Valign = Align.Center;
        icon.Vexpand = true;
        thumbnail.Append(icon);

        var duration = Label.New(FormatDuration(video.Duration));
        duration.Halign = Align.End;
        duration.MarginEnd = 10;
        duration.MarginBottom = 8;
        duration.CssClasses = ["caption", "dim-label"];
        thumbnail.Append(duration);

        return thumbnail;
    }

    private MenuButton CreateVideoMenuButton(VideoSummary video)
    {
        var menuButton = MenuButton.New();
        menuButton.IconName = "view-more-symbolic";
        menuButton.TooltipText = $"More actions for {video.Title}";
        menuButton.Valign = Align.Start;
        menuButton.CssClasses = ["flat"];

        var menuBox = Box.New(Orientation.Vertical, 4);
        menuBox.MarginTop = 6;
        menuBox.MarginBottom = 6;
        menuBox.MarginStart = 6;
        menuBox.MarginEnd = 6;

        menuBox.Append(CreatePopoverAction("Play", () => PlayVideo(video)));
        menuBox.Append(CreatePopoverAction("Add to queue", () => AddToQueue(video)));
        menuBox.Append(CreatePopoverAction("Add next", () => AddNext(video)));
        menuBox.Append(CreatePopoverAction("Open channel", () => SetStatus($"Open channel stub: {video.ChannelName}")));
        menuBox.Append(CreatePopoverAction("Copy link", () => CopyVideoLink(video)));

        var popover = Popover.New();
        popover.Child = menuBox;
        menuButton.Popover = popover;

        return menuButton;
    }

    private Widget CreateSearchPage()
    {
        var page = Box.New(Orientation.Vertical, 12);
        page.MarginStart = 24;
        page.MarginEnd = 24;
        page.MarginTop = 24;
        page.MarginBottom = 24;
        page.Hexpand = true;
        page.Vexpand = true;

        var title = Label.New("Search");
        title.Xalign = 0;
        title.CssClasses = ["title-2"];
        page.Append(title);
        page.Append(_searchSummaryLabel);

        var scrolledWindow = ScrolledWindow.New();
        scrolledWindow.Hexpand = true;
        scrolledWindow.Vexpand = true;
        scrolledWindow.Child = _searchResultsFlowBox;
        page.Append(scrolledWindow);
        return page;
    }

    private Widget CreatePlaceholderPage(string titleText, string description)
    {
        var statusPage = Adw.StatusPage.New();
        statusPage.Title = titleText;
        statusPage.Description = description;
        statusPage.IconName = "applications-internet-symbolic";
        return statusPage;
    }

    private Popover BuildSearchPopover()
    {
        _searchEntry.PlaceholderText = "Search or paste a YouTube URL";
        _searchEntry.WidthChars = 36;
        _searchEntry.OnActivate += (_, _) => SubmitSearchText();

        var popoverBox = Box.New(Orientation.Vertical, 10);
        popoverBox.MarginTop = 12;
        popoverBox.MarginBottom = 12;
        popoverBox.MarginStart = 12;
        popoverBox.MarginEnd = 12;
        popoverBox.Append(_searchEntry);
        popoverBox.Append(CreateDimLabel("Enter searches YouTube with yt-dlp or opens a supported YouTube URL."));

        var popover = Popover.New();
        popover.Child = popoverBox;
        popover.SetParent(_searchButton);
        return popover;
    }

    private void BuildAccountPopover()
    {
        var session = _sessionService.GetCurrentSession();

        var popoverBox = Box.New(Orientation.Vertical, 10);
        popoverBox.MarginTop = 12;
        popoverBox.MarginBottom = 12;
        popoverBox.MarginStart = 12;
        popoverBox.MarginEnd = 12;

        var headingText = session.IsSignedIn ? (session.DisplayName ?? "Signed in") : "Not signed in";
        var heading = Label.New(headingText);
        heading.Xalign = 0;
        heading.CssClasses = ["heading"];
        popoverBox.Append(heading);

        popoverBox.Append(CreateDimLabel("Account support will use manual session/cookie entry in a later step."));

        var sessionButton = Button.NewWithLabel("Add manual session/cookie");
        sessionButton.Sensitive = false;
        popoverBox.Append(sessionButton);

        var popover = Popover.New();
        popover.Child = popoverBox;
        _accountButton.Popover = popover;
    }

    private void BuildAppMenuPopover()
    {
        var menuBox = Box.New(Orientation.Vertical, 4);
        menuBox.MarginTop = 6;
        menuBox.MarginBottom = 6;
        menuBox.MarginStart = 6;
        menuBox.MarginEnd = 6;

        menuBox.Append(CreatePopoverAction("Preferences", () => SetStatus("Preferences stub")));
        menuBox.Append(CreatePopoverAction("About SilverScreen", () => SetStatus("About stub: SilverScreen")));
        menuBox.Append(CreatePopoverAction("Quit", () => Widget.Close()));

        var popover = Popover.New();
        popover.Child = menuBox;
        _appMenuButton.Popover = popover;
    }

    private void BuildQueueButton()
    {
        var buttonChild = Box.New(Orientation.Horizontal, 6);
        buttonChild.MarginStart = 10;
        buttonChild.MarginEnd = 10;
        buttonChild.MarginTop = 6;
        buttonChild.MarginBottom = 6;

        var icon = Image.NewFromIconName("playlist-symbolic");
        icon.PixelSize = 16;
        buttonChild.Append(icon);
        buttonChild.Append(_queueDurationLabel);
        _queueButton.Child = buttonChild;

        var popoverBox = Box.New(Orientation.Vertical, 8);
        popoverBox.MarginTop = 10;
        popoverBox.MarginBottom = 10;
        popoverBox.MarginStart = 10;
        popoverBox.MarginEnd = 10;
        popoverBox.Append(_queueItemsBox);
        popoverBox.Append(CreatePopoverAction("Clear queue", _queueService.Clear));

        var popover = Popover.New();
        popover.Child = popoverBox;
        _queueButton.Popover = popover;

        RefreshQueueUi();
    }

    private void RefreshQueueUi()
    {
        _queueButton.Visible = _queueService.Items.Count > 0;
        _queueDurationLabel.Label_ = FormatQueuedDuration(_queueService.TotalDuration);
        ClearBox(_queueItemsBox);

        foreach (var item in _queueService.Items)
        {
            _queueItemsBox.Append(CreateQueueRow(item));
        }
    }

    private Widget CreateQueueRow(QueueItem item)
    {
        var row = Box.New(Orientation.Horizontal, 8);
        row.WidthRequest = 320;

        var title = Label.New(item.Video.Title);
        title.Xalign = 0;
        title.Hexpand = true;
        title.Wrap = true;
        title.MaxWidthChars = 34;
        row.Append(title);

        var remove = Button.NewFromIconName("user-trash-symbolic");
        remove.TooltipText = "Remove from queue";
        remove.CssClasses = ["flat", "circular"];
        remove.OnClicked += (_, _) => _queueService.Remove(item);
        row.Append(remove);

        return row;
    }

    private static void ClearBox(Box box)
    {
        while (box.GetFirstChild() is { } child)
        {
            box.Remove(child);
        }
    }

    private static void ClearFlowBox(FlowBox flowBox)
    {
        while (flowBox.GetFirstChild() is { } child)
        {
            flowBox.Remove(child);
        }
    }

    private Button CreatePopoverAction(string label, Action action)
    {
        var button = Button.NewWithLabel(label);
        button.Halign = Align.Fill;
        button.CssClasses = ["flat"];
        button.OnClicked += (_, _) => action();
        return button;
    }

    private Label CreateDimLabel(string text)
    {
        var label = Label.New(text);
        label.Xalign = 0;
        label.Wrap = true;
        label.CssClasses = ["dim-label"];
        return label;
    }

    private void OnSearchButton_Clicked(object? sender, EventArgs e)
    {
        _searchPopover.Popup();
        _searchEntry.GrabFocus();
    }

    private async void SubmitSearchText()
    {
        var text = _searchEntry.GetText().Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("Empty search ignored.");
            return;
        }

        var parsedUrl = YouTubeUrlParser.Parse(text);
        switch (parsedUrl.Kind)
        {
            case YouTubeUrlKind.Video:
                await PlayYouTubeUrl(parsedUrl);
                break;
            case YouTubeUrlKind.Shorts:
                SetStatus("Shorts are not supported in SilverScreen.");
                break;
            case YouTubeUrlKind.Channel:
                SetStatus("Channel pages are not implemented yet.");
                break;
            case YouTubeUrlKind.Playlist:
                SetStatus("Playlists are not implemented yet.");
                break;
            case YouTubeUrlKind.UnknownYouTube:
                SetStatus("Unsupported YouTube URL.");
                break;
            case YouTubeUrlKind.Invalid:
                SetStatus("Invalid YouTube URL.");
                break;
            case YouTubeUrlKind.NotYouTube:
                await SearchPlainText(text);
                break;
            default:
                throw new InvalidOperationException($"Unhandled YouTube URL kind: {parsedUrl.Kind}");
        }

        _searchPopover.Popdown();
    }

    private async Task SearchPlainText(string query)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        var cancellationToken = _searchCancellation.Token;

        _viewStack.VisibleChildName = SearchTabName;
        ClearFlowBox(_searchResultsFlowBox);
        _searchSummaryLabel.Label_ = $"Searching YouTube for “{query}”…";
        SetStatus($"Searching YouTube for “{query}”…");

        try
        {
            var result = await _searchService.SearchAsync(new SearchRequest(query), cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            RenderSearchResults(result);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RenderSearchResults(SearchResultPage result)
    {
        ClearFlowBox(_searchResultsFlowBox);
        foreach (var video in result.Videos)
        {
            _searchResultsFlowBox.Append(CreateVideoCard(video));
        }

        var message = result.StatusMessage ?? (result.IsSuccess ? "Search complete." : "Search failed.");
        _searchSummaryLabel.Label_ = message;
        SetStatus(message);
    }

    private async Task PlayYouTubeUrl(YouTubeUrlParseResult parsedUrl)
    {
        if (parsedUrl.VideoId is null || parsedUrl.CanonicalWatchUrl is null)
        {
            SetStatus("Invalid YouTube URL.");
            return;
        }

        var video = new VideoSummary(
            parsedUrl.VideoId,
            $"YouTube video {parsedUrl.VideoId}",
            "YouTube",
            TimeSpan.Zero,
            string.Empty,
            false,
            parsedUrl.CanonicalWatchUrl);

        SetStatus(await _playbackService.PlayAsync(new PlaybackRequest(video)));
    }

    private async void PlayVideo(VideoSummary video)
    {
        SetStatus(await _playbackService.PlayAsync(new PlaybackRequest(video)));
    }

    private void AddToQueue(VideoSummary video)
    {
        _queueService.Add(video);
        SetStatus($"Queued: {video.Title}");
    }

    private void AddNext(VideoSummary video)
    {
        _queueService.AddNext(video);
        SetStatus($"Added next: {video.Title}");
    }

    private void SetStatus(string message)
    {
        Console.WriteLine(message);
        _statusLabel.Label_ = message;
    }


    private void CopyVideoLink(VideoSummary video)
    {
        var videoUrl = BuildVideoUrl(video);
        SetStatus(videoUrl is null ? "No playable URL is available for this mock video yet." : $"Copy link stub: {videoUrl}");
    }

    private static bool HasPlayableUrl(VideoSummary video) => BuildVideoUrl(video) is not null;

    private static string? BuildVideoUrl(VideoSummary video) => string.IsNullOrWhiteSpace(video.WatchUrl) ? PlaybackRequest.BuildWatchUrl(video.Id) : video.WatchUrl;

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes}:{duration.Seconds:00}";
    }

    private static string FormatQueuedDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes:00}m";
        }

        return duration.TotalMinutes >= 1
            ? $"{(int)duration.TotalMinutes}m"
            : $"{duration.Seconds}s";
    }
}
