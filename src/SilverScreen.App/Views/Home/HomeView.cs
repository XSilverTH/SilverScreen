using System.Runtime.CompilerServices;
using Adw;
using Gtk;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.ViewModels;
using SilverScreen.Views.Components;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;

namespace SilverScreen.Views.Home;

public partial class HomeView : ViewBase<Box>
{
    private readonly ConditionalWeakTable<ListItem, VideoCardView> _cardsByListItem = new();
    private readonly ILogger _logger = Log.ForContext<HomeView>();
    private readonly ScrolledWindow _scrolledWindow;
    private readonly Adjustment? _vadjustment;
    private readonly Box _searchBar;
    private readonly SearchEntry _searchEntry;
    private readonly SearchViewModel _searchViewModel;
    private readonly Box _statusHost;
    private readonly Box _statusLoadingPage;
    private readonly StatusPage _statusPage;
    private readonly IThumbnailService _thumbnails;
    private readonly VideoCardActions _videoActions;
    private readonly SignalListItemFactory _videoFactory;
    private readonly GridView _videoGrid;
    private readonly StringList _videoIds;
    private readonly NoSelection _videoSelection;
    private readonly Dictionary<string, VideoSummary> _videosById = [];
    private readonly HomeViewModel _viewModel;
    private VideoSummary[] _displayedVideos = [];
    private bool _disposed;

    public HomeView(
        HomeViewModel viewModel,
        SearchViewModel searchViewModel,
        IThumbnailService thumbnails,
        VideoCardActions videoActions)
    {
        _viewModel = viewModel;
        _searchViewModel = searchViewModel;
        _thumbnails = thumbnails;
        _videoActions = videoActions;
        _searchBar = GetRequiredObject<Box>("home_search_bar");
        _searchEntry = GetRequiredObject<SearchEntry>("home_search_entry");
        _statusHost = GetRequiredObject<Box>("home_status_host");
        _statusLoadingPage = GetRequiredObject<Box>("home_status_loading_page");
        _statusPage = GetRequiredObject<StatusPage>("home_status_page");
        _scrolledWindow = GetRequiredObject<ScrolledWindow>("home_scrolled_window");
        _vadjustment = _scrolledWindow.Vadjustment;
        if (_vadjustment is not null)
            _vadjustment.OnValueChanged += OnScrollValueChanged;

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
        _searchViewModel.StateChanged += OnSearchStateChanged;
        Render(_viewModel.State);
    }

    public bool IsLoading => _viewModel.State is { IsLoading: true } or { IsLoadingMore: true };

    public bool IsSearchActive { get; private set; }

    public event EventHandler<bool>? SearchModeChanged;

    public event EventHandler<bool>? RefreshLoadingChanged;

    public Task RefreshAsync()
    {
        return _viewModel.State is
        { Kind: not HomeFeedStateKind.SignedOut, IsLoading: false, IsLoadingMore: false }
            ? _viewModel.RefreshAsync()
            : Task.CompletedTask;
    }

    public void ActivateSearch()
    {
        if (_disposed || IsSearchActive)
            return;

        IsSearchActive = true;
        _searchBar.Visible = true;
        _searchViewModel.Reset();
        RenderSearch(_searchViewModel.State);
        SearchModeChanged?.Invoke(this, true);
        _searchEntry.GrabFocus();
    }

    public void ReturnToHome()
    {
        if (_disposed || !IsSearchActive)
            return;

        IsSearchActive = false;
        _searchViewModel.Reset();
        _searchEntry.SetText(string.Empty);
        _searchBar.Visible = false;
        Render(_viewModel.State);
        SearchModeChanged?.Invoke(this, false);
    }

    private async void OnHomeSearchEntryActivated(object? sender, EventArgs args)
    {
        try
        {
            await _searchViewModel.SubmitAsync(_searchEntry.GetText());
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to submit search query {GetText}", _searchEntry.GetText());
        }
    }

    private void OnScrollValueChanged(object? sender, EventArgs args)
    {
        if (_disposed || _vadjustment is null ||
            _vadjustment.Value + _vadjustment.PageSize < _vadjustment.Upper - 240)
            return;

        if (IsSearchActive)
            _ = _searchViewModel.LoadMoreAsync();
        else
            _ = _viewModel.LoadMoreAsync();
    }


    private void OnStateChanged(object? sender, HomeFeedState state)
    {
        Functions.IdleAdd(0, () =>
        {
            if (_disposed) return false;
            RefreshLoadingChanged?.Invoke(this, state.IsLoading || state.IsLoadingMore);
            if (!IsSearchActive)
                Render(state);

            return false;
        });
    }

    private void OnSearchStateChanged(object? sender, SearchViewState state)
    {
        Functions.IdleAdd(0, () =>
        {
            if (!_disposed && IsSearchActive)
                RenderSearch(state);

            return false;
        });
    }

    private void Render(HomeFeedState state)
    {
        ApplyVideos(state.Videos);

        var hasDisplayedVideos = _displayedVideos.Length > 0;
        _statusHost.Visible = false;
        _scrolledWindow.Visible = false;

        if (!hasDisplayedVideos)
        {
            ShowStatus(state);
            return;
        }

        _scrolledWindow.Visible = true;
    }

    private void RenderSearch(SearchViewState state)
    {

        ApplyVideos(state.Videos);
        _statusHost.Visible = false;
        _scrolledWindow.Visible = false;

        if (_displayedVideos.Length > 0)
        {
            _scrolledWindow.Visible = true;
            return;
        }

        _statusLoadingPage.Visible = state.IsLoading;
        _statusPage.Visible = !state.IsLoading;
        if (!state.IsLoading)
        {
            _statusPage.Title = "Search";
            _statusPage.Description = state.Summary;
            _statusPage.IconName = "system-search-symbolic";
        }

        _statusHost.Visible = true;
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

        var card = new VideoCardView(_thumbnails, _videoActions);
        listItem.Child = card.Widget;
        _cardsByListItem.Add(listItem, card);
    }

    private void OnVideoCardBind(object? sender, SignalListItemFactory.BindSignalArgs args)
    {
        if (args.Object is not ListItem { Item: StringObject { String: { } id } } listItem ||
            !_cardsByListItem.TryGetValue(listItem, out var card) ||
            !_videosById.TryGetValue(id, out var video))
            return;

        card.Bind(video);
    }

    private void OnVideoCardUnbind(object? sender, SignalListItemFactory.UnbindSignalArgs args)
    {
        if (args.Object is ListItem listItem && _cardsByListItem.TryGetValue(listItem, out var card))
            card.Unbind();
    }

    private void OnVideoCardTeardown(object? sender, SignalListItemFactory.TeardownSignalArgs args)
    {
        if (args.Object is not ListItem listItem || !_cardsByListItem.TryGetValue(listItem, out var card))
            return;

        _cardsByListItem.Remove(listItem);
        DisposeVideoCardCell(listItem, card);
    }

    private static void DisposeVideoCardCell(ListItem listItem, VideoCardView card)
    {
        listItem.Child = null;
        card.Dispose();
    }

    public new void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _viewModel.StateChanged -= OnStateChanged;
        _searchViewModel.StateChanged -= OnSearchStateChanged;
        if (_vadjustment is not null)
            _vadjustment.OnValueChanged -= OnScrollValueChanged;
        _searchViewModel.Dispose();

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