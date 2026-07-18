using Gtk;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.ViewModels;
using SilverScreen.Views.Components;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Views.Home;

public partial class HomeView : ViewBase<Box>
{
    private readonly HomeViewModel _viewModel;
    private readonly IThumbnailService _thumbnails;
    private readonly VideoCardActions _videoActions;
    private readonly Label _loadingLabel;
    private readonly Widget _loadingRow;
    private readonly Label _messageLabel;
    private readonly Box _statusHost;
    private readonly ScrolledWindow _scrolledWindow;
    private readonly Button _loadMoreButton;
    private readonly StringList _videoIds;
    private readonly NoSelection _videoSelection;
    private readonly SignalListItemFactory _videoFactory;
    private readonly GridView _videoGrid;
    private readonly Dictionary<string, VideoSummary> _videosById = [];
    private readonly Dictionary<Widget, VideoCardView> _cardsByCell = [];
    private IReadOnlyList<VideoSummary> _displayedVideos = [];
    private bool _disposed;

    public HomeView(HomeViewModel viewModel, IThumbnailService thumbnails, VideoCardActions videoActions)
    {
        _viewModel = viewModel;
        _thumbnails = thumbnails;
        _videoActions = videoActions;
        _loadingRow = GetRequiredObject<Widget>("home_loading_row");
        _loadingLabel = GetRequiredObject<Label>("home_loading_label");
        _messageLabel = GetRequiredObject<Label>("home_message_label");
        _statusHost = GetRequiredObject<Box>("home_status_host");
        _scrolledWindow = GetRequiredObject<ScrolledWindow>("home_scrolled_window");
        _loadMoreButton = GetRequiredObject<Button>("home_load_more_button");

        _videoIds = StringList.New([]);
        _videoSelection = NoSelection.New(_videoIds);
        _videoFactory = SignalListItemFactory.New();
        _videoFactory.OnSetup += OnVideoCardSetup;
        _videoFactory.OnBind += OnVideoCardBind;
        _videoFactory.OnUnbind += OnVideoCardUnbind;
        _videoFactory.OnTeardown += OnVideoCardTeardown;
        _videoGrid = GridView.New(_videoSelection, _videoFactory);
        _videoGrid.MinColumns = 1;
        _videoGrid.MaxColumns = 4;
        _videoGrid.SingleClickActivate = false;
        _videoGrid.MarginStart = 24;
        _videoGrid.MarginEnd = 24;
        _videoGrid.MarginTop = 12;
        _videoGrid.Hexpand = true;
        _videoGrid.Vexpand = true;
        _scrolledWindow.Child = _videoGrid;

        _viewModel.StateChanged += OnStateChanged;
        Render(_viewModel.State);
    }

    public Task RefreshAsync() => _viewModel.State is
        { Kind: not HomeFeedStateKind.SignedOut, IsLoading: false, IsLoadingMore: false }
        ? _viewModel.RefreshAsync()
        : Task.CompletedTask;

    private void OnHomeLoadMoreButtonClicked(object? sender, EventArgs args) => _ = _viewModel.LoadMoreAsync();

    private void OnStateChanged(object? sender, HomeFeedState state)
    {
        GLib.Functions.IdleAdd(0, () =>
        {
            if (!_disposed)
                Render(state);

            return false;
        });
    }

    private void Render(HomeFeedState state)
    {
        if (!state.IsLoading && !state.IsLoadingMore)
            ApplyVideos(NormalizeVideos(state.Videos));

        var hasDisplayedVideos = _displayedVideos.Count > 0;
        var isLoading = state.IsLoading || state.IsLoadingMore;
        _loadingRow.Visible = false;
        _messageLabel.Visible = false;
        _statusHost.Visible = false;
        _scrolledWindow.Visible = false;
        _loadMoreButton.Visible = false;

        if (!hasDisplayedVideos)
        {
            ShowStatus(state);
            return;
        }

        _scrolledWindow.Visible = true;
        if (isLoading)
        {
            _loadingLabel.SetText(state.IsLoadingMore
                ? "Loading more recommendations…"
                : "Loading YouTube recommendations…");
            _loadingRow.Visible = true;
        }
        else if (!string.IsNullOrEmpty(state.Message))
        {
            _messageLabel.SetText(state.Message);
            _messageLabel.Visible = true;
        }

        if (state.HasContinuation)
        {
            _loadMoreButton.Label = isLoading && state.IsLoadingMore ? "Loading more…" : "Load more";
            _loadMoreButton.Sensitive = !isLoading;
            _loadMoreButton.Visible = true;
        }
    }

    private void ShowStatus(HomeFeedState state)
    {
        Clear(_statusHost);
        _statusHost.Append(state.Kind switch
        {
            HomeFeedStateKind.InitialLoading => LoadingPage(),
            HomeFeedStateKind.SignedOut => StatusPage("Home", "Sign in to see your YouTube recommendations.",
                "avatar-default-symbolic"),
            HomeFeedStateKind.Empty or HomeFeedStateKind.Ready => StatusPage("Home",
                "No recommendations are available right now.", "applications-internet-symbolic"),
            HomeFeedStateKind.AuthenticationRequired => StatusPage("Home",
                "Your YouTube session is no longer valid.", "dialog-password-symbolic"),
            _ => StatusPage("Home", "Could not load YouTube recommendations.", "network-error-symbolic")
        });
        _statusHost.Visible = true;
    }

    private static IReadOnlyList<VideoSummary> NormalizeVideos(IReadOnlyList<VideoSummary> videos) =>
        videos.Where(video => !video.IsShort).GroupBy(video => video.Id).Select(group => group.First()).ToArray();

    private void ApplyVideos(IReadOnlyList<VideoSummary> videos)
    {
        var nextVideos = videos.ToArray();
        var prefixLength = 0;
        while (prefixLength < _displayedVideos.Count && prefixLength < nextVideos.Length &&
               _displayedVideos[prefixLength] == nextVideos[prefixLength])
        {
            prefixLength++;
        }

        var suffixLength = 0;
        while (_displayedVideos.Count - suffixLength > prefixLength &&
               nextVideos.Length - suffixLength > prefixLength &&
               _displayedVideos[_displayedVideos.Count - suffixLength - 1] ==
               nextVideos[nextVideos.Length - suffixLength - 1])
        {
            suffixLength++;
        }

        var removedMiddleCount = _displayedVideos.Count - prefixLength - suffixLength;
        var addedMiddleCount = nextVideos.Length - prefixLength - suffixLength;
        _videosById.Clear();
        foreach (var video in nextVideos)
            _videosById[video.Id] = video;

        _displayedVideos = nextVideos;
        if (removedMiddleCount == 0 && addedMiddleCount == 0)
            return;

        var addedMiddleIds = nextVideos.Skip(prefixLength).Take(addedMiddleCount).Select(video => video.Id).ToArray();
        _videoIds.Splice((uint)prefixLength, (uint)removedMiddleCount, addedMiddleIds);
    }

    private void OnVideoCardSetup(object? sender, SignalListItemFactory.SetupSignalArgs args)
    {
        if (args.Object is not ListItem listItem)
            return;

        var cell = Box.New(Orientation.Vertical, 0);
        cell.MarginStart = 10;
        cell.MarginEnd = 10;
        cell.MarginTop = 12;
        cell.MarginBottom = 12;
        var card = new VideoCardView(_thumbnails, _videoActions);
        cell.Append(card.Widget);
        listItem.Child = cell;
        _cardsByCell[cell] = card;
    }

    private void OnVideoCardBind(object? sender, SignalListItemFactory.BindSignalArgs args)
    {
        if (args.Object is not ListItem { Child: { } child, Item: StringObject videoId } ||
            videoId.String is not { } id ||
            !_cardsByCell.TryGetValue(child, out var card) ||
            !_videosById.TryGetValue(id, out var video))
        {
            return;
        }

        card.Bind(video);
    }

    private void OnVideoCardUnbind(object? sender, SignalListItemFactory.UnbindSignalArgs args)
    {
        if (args.Object is ListItem { Child: { } child } && _cardsByCell.TryGetValue(child, out var card))
            card.Unbind();
    }

    private void OnVideoCardTeardown(object? sender, SignalListItemFactory.TeardownSignalArgs args)
    {
        if (args.Object is not ListItem { Child: { } child } || !_cardsByCell.Remove(child, out var card))
            return;

        card.Unbind();
        card.Dispose();
    }

    private static Box LoadingPage()
    {
        var content = Box.New(Orientation.Vertical, 12);
        content.Halign = Align.Center;
        content.Valign = Align.Center;
        content.Hexpand = true;
        content.Vexpand = true;
        var spinner = Spinner.New();
        spinner.Halign = Align.Center;
        spinner.Spinning = true;
        content.Append(spinner);
        content.Append(DimLabel("Loading YouTube recommendations…"));
        return content;
    }

    private static Widget StatusPage(string title, string description, string icon)
    {
        var page = Adw.StatusPage.New();
        page.Title = title;
        page.Description = description;
        page.IconName = icon;
        page.Hexpand = true;
        page.Vexpand = true;
        return page;
    }

    private static Label DimLabel(string text)
    {
        var label = Label.New(text);
        label.Xalign = 0;
        label.Wrap = true;
        label.CssClasses = ["dim-label"];
        return label;
    }

    private static void Clear(Box box)
    {
        while (box.GetFirstChild() is { } child)
            box.Remove(child);
    }

    public new void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _viewModel.StateChanged -= OnStateChanged;
        _videoFactory.OnSetup -= OnVideoCardSetup;
        _videoFactory.OnBind -= OnVideoCardBind;
        _videoFactory.OnUnbind -= OnVideoCardUnbind;
        _videoFactory.OnTeardown -= OnVideoCardTeardown;
        _scrolledWindow.Child = null;
        foreach (var card in _cardsByCell.Values)
            card.Dispose();

        _cardsByCell.Clear();
        _videoGrid.Dispose();
        _videoSelection.Dispose();
        _videoFactory.Dispose();
        _videoIds.Dispose();
        _viewModel.Dispose();
        base.Dispose();
    }
}