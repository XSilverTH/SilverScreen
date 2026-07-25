using System.Runtime.CompilerServices;
using Adw;
using Gtk;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.ViewModels;
using SilverScreen.Views.Components;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;

namespace SilverScreen.Views.Home;

public partial class HomeView : ViewBase<Box>
{
    private sealed class VideoCardCell(Box cell, VideoCardView card)
    {
        public Box Cell { get; } = cell;
        public VideoCardView Card { get; } = card;
    }

    private readonly ConditionalWeakTable<ListItem, VideoCardCell> _cardsByListItem = new();
    private readonly Button _loadMoreButton;
    private readonly ScrolledWindow _scrolledWindow;
    private readonly Box _statusHost;
    private readonly Box _statusLoadingPage;
    private readonly StatusPage _statusPage;
    private readonly IThumbnailService _thumbnails;
    private readonly VideoCardActions _videoActions;
    private readonly SignalListItemFactory _videoFactory;
    private readonly GridView _videoGrid;
    private readonly StringList _videoIds;
    private readonly Dictionary<string, VideoSummary> _videosById = [];
    private readonly NoSelection _videoSelection;
    private readonly HomeViewModel _viewModel;
    private VideoSummary[] _displayedVideos = [];
    private bool _disposed;

    public HomeView(HomeViewModel viewModel, IThumbnailService thumbnails, VideoCardActions videoActions)
    {
        _viewModel = viewModel;
        _thumbnails = thumbnails;
        _videoActions = videoActions;
        _statusHost = GetRequiredObject<Box>("home_status_host");
        _statusLoadingPage = GetRequiredObject<Box>("home_status_loading_page");
        _statusPage = GetRequiredObject<StatusPage>("home_status_page");
        _scrolledWindow = GetRequiredObject<ScrolledWindow>("home_scrolled_window");
        _loadMoreButton = GetRequiredObject<Button>("home_load_more_button");

        _videoIds = StringList.New([]);
        _videoSelection = NoSelection.New(_videoIds);
        _videoFactory = SignalListItemFactory.New();
        _videoFactory.OnSetup += OnVideoCardSetup;
        _videoFactory.OnBind += OnVideoCardBind;
        _videoFactory.OnUnbind += OnVideoCardUnbind;
        _videoFactory.OnTeardown += OnVideoCardTeardown;
        _videoGrid = GetRequiredObject<GridView>("home_video_grid");
        _videoGrid.Model = _videoSelection;
        _videoGrid.Factory = _videoFactory;

        _viewModel.StateChanged += OnStateChanged;
        Render(_viewModel.State);
    }

    public bool IsLoading => _viewModel.State is { IsLoading: true } or { IsLoadingMore: true };

    public event EventHandler<bool>? RefreshLoadingChanged;

    public Task RefreshAsync()
    {
        return _viewModel.State is
            { Kind: not HomeFeedStateKind.SignedOut, IsLoading: false, IsLoadingMore: false }
            ? _viewModel.RefreshAsync()
            : Task.CompletedTask;
    }

    private void OnHomeLoadMoreButtonClicked(object? sender, EventArgs args)
    {
        _ = _viewModel.LoadMoreAsync();
    }

    private void OnStateChanged(object? sender, HomeFeedState state)
    {
        Functions.IdleAdd(0, () =>
        {
            if (!_disposed)
                Render(state);

            return false;
        });
    }

    private void Render(HomeFeedState state)
    {
        if (state is { IsLoading: false, IsLoadingMore: false })
            ApplyVideos(NormalizeVideos(state.Videos));

        var hasDisplayedVideos = _displayedVideos.Length > 0;
        var isLoading = state.IsLoading || state.IsLoadingMore;
        _statusHost.Visible = false;
        _scrolledWindow.Visible = false;
        _loadMoreButton.Visible = false;
        RefreshLoadingChanged?.Invoke(this, isLoading);

        if (!hasDisplayedVideos)
        {
            ShowStatus(state);
            return;
        }

        _scrolledWindow.Visible = true;

        if (!state.HasContinuation) return;
        _loadMoreButton.Label = isLoading && state.IsLoadingMore ? "Loading more…" : "Load more";
        _loadMoreButton.Sensitive = !isLoading;
        _loadMoreButton.Visible = true;
    }

    private void ShowStatus(HomeFeedState state)
    {
        _statusLoadingPage.Visible = false;
        _statusPage.Visible = false;

        if (state.Kind == HomeFeedStateKind.InitialLoading)
        {
            _statusLoadingPage.Visible = true;
        }
        else
        {
            var (description, icon) = state.Kind switch
            {
                HomeFeedStateKind.SignedOut => ("Sign in to see your YouTube recommendations.",
                    "avatar-default-symbolic"),
                HomeFeedStateKind.Empty or HomeFeedStateKind.Ready => ("No recommendations are available right now.",
                    "applications-internet-symbolic"),
                HomeFeedStateKind.AuthenticationRequired => ("Your YouTube session is no longer valid.",
                    "dialog-password-symbolic"),
                _ => ("Could not load YouTube recommendations.", "network-error-symbolic")
            };
            _statusPage.Title = "Home";
            _statusPage.Description = description;
            _statusPage.IconName = icon;
            _statusPage.Visible = true;
        }

        _statusHost.Visible = true;
    }

    private static VideoSummary[] NormalizeVideos(VideoSummary[] videos)
    {
        return videos.Where(video => !video.IsShort).GroupBy(video => video.Id).Select(group => group.First())
            .ToArray();
    }

    private void ApplyVideos(IReadOnlyList<VideoSummary> videos)
    {
        var nextVideos = videos.ToArray();
        var prefixLength = 0;
        while (prefixLength < _displayedVideos.Length && prefixLength < nextVideos.Length &&
               _displayedVideos[prefixLength] == nextVideos[prefixLength])
            prefixLength++;

        var suffixLength = 0;
        while (_displayedVideos.Length - suffixLength > prefixLength &&
               nextVideos.Length - suffixLength > prefixLength &&
               _displayedVideos[_displayedVideos.Length - suffixLength - 1] ==
               nextVideos[nextVideos.Length - suffixLength - 1])
            suffixLength++;

        var removedMiddleCount = _displayedVideos.Length - prefixLength - suffixLength;
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
        _cardsByListItem.Add(listItem, new VideoCardCell(cell, card));
    }

    private void OnVideoCardBind(object? sender, SignalListItemFactory.BindSignalArgs args)
    {
        if (args.Object is not ListItem { Item: StringObject { String: { } id } } listItem ||
            !_cardsByListItem.TryGetValue(listItem, out var cell) ||
            !_videosById.TryGetValue(id, out var video))
            return;

        cell.Card.Bind(video);
    }

    private void OnVideoCardUnbind(object? sender, SignalListItemFactory.UnbindSignalArgs args)
    {
        if (args.Object is ListItem listItem && _cardsByListItem.TryGetValue(listItem, out var cell))
            cell.Card.Unbind();
    }

    private void OnVideoCardTeardown(object? sender, SignalListItemFactory.TeardownSignalArgs args)
    {
        if (args.Object is not ListItem listItem || !_cardsByListItem.TryGetValue(listItem, out var cell))
            return;

        _cardsByListItem.Remove(listItem);
        DisposeVideoCardCell(listItem, cell);
    }

    private static void DisposeVideoCardCell(ListItem listItem, VideoCardCell cell)
    {
        listItem.Child = null;
        cell.Cell.Remove(cell.Card.Widget);
        cell.Card.Dispose();
        cell.Cell.Dispose();
    }

    public new void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _viewModel.StateChanged -= OnStateChanged;

        // Dropping the factory tears down its live/recycled list items while the
        // lifecycle handlers are still connected.  Any defensive leftovers are
        // weakly keyed and disposed below without retaining historical cells.
        _videoGrid.Factory = null;
        foreach (var association in _cardsByListItem)
            DisposeVideoCardCell(association.Key, association.Value);
        _cardsByListItem.Clear();

        _videoFactory.OnSetup -= OnVideoCardSetup;
        _videoFactory.OnBind -= OnVideoCardBind;
        _videoFactory.OnUnbind -= OnVideoCardUnbind;
        _videoFactory.OnTeardown -= OnVideoCardTeardown;
        _scrolledWindow.Child = null;
        _videoGrid.Dispose();
        _videoSelection.Dispose();
        _videoFactory.Dispose();
        _videoIds.Dispose();
        _viewModel.Dispose();
        base.Dispose();
    }
}