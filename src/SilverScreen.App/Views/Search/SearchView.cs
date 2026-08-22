using System.Runtime.CompilerServices;
using Adw;
using Gtk;

using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.ViewModels;
using SilverScreen.Views.Components;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;

namespace SilverScreen.Views.Search;

public partial class SearchView : ViewBase<Box>
{

    private readonly Stack _stack;
    private readonly Label _loadingLabel;
    private readonly GridView _videoGrid;
    private readonly StatusPage _emptyPage;
    private readonly StatusPage _errorPage;

    private readonly Adjustment? _vadjustment;
    private readonly Revealer _paginationLoadingRevealer;

    private readonly SearchViewModel _viewModel;
    private readonly IThumbnailService _thumbnails;
    private readonly IWatchProgressService _watchProgress;
    private readonly VideoCardActions _videoActions;

    private readonly ConditionalWeakTable<ListItem, VideoCardView> _cardsByListItem = new();
    private readonly SignalListItemFactory _videoFactory;
    private readonly StringList _videoIds;
    private readonly NoSelection _videoSelection;
    private readonly Dictionary<string, VideoSummary> _videosById = [];

    private VideoSummary[] _displayedVideos = [];
    private bool _disposed;


    public event EventHandler<bool>? RefreshLoadingChanged;

    public SearchView(
        SearchViewModel viewModel,
        IThumbnailService thumbnails,
        IWatchProgressService watchProgress,
        VideoCardActions videoActions)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
        _watchProgress = watchProgress ?? throw new ArgumentNullException(nameof(watchProgress));
        _videoActions = videoActions ?? throw new ArgumentNullException(nameof(videoActions));

        _stack = GetRequiredObject<Stack>("search_stack");
        _loadingLabel = GetRequiredObject<Label>("search_loading_label");
        _videoGrid = GetRequiredObject<GridView>("search_video_grid");
        _emptyPage = GetRequiredObject<StatusPage>("search_empty_page");
        var scrolledWindow = GetRequiredObject<ScrolledWindow>("search_scrolled_window");
        _paginationLoadingRevealer = GetRequiredObject<Revealer>("search_pagination_loading_revealer");
        _errorPage = GetRequiredObject<StatusPage>("search_error_page");

        _vadjustment = scrolledWindow.Vadjustment;
        if (_vadjustment is not null)
        {
            _vadjustment.OnValueChanged += OnScrollValueChanged;
        }

        _videoIds = StringList.New([]);
        _videoSelection = NoSelection.New(_videoIds);
        _videoFactory = SignalListItemFactory.New();
        _videoFactory.OnSetup += OnVideoCardSetup;
        _videoFactory.OnBind += OnVideoCardBind;
        _videoFactory.OnUnbind += OnVideoCardUnbind;
        _videoFactory.OnTeardown += OnVideoCardTeardown;
        _videoGrid.Model = _videoSelection;
        _videoGrid.Factory = _videoFactory;

        _viewModel.StateChanged += OnStateChanged;
        Render(_viewModel.State);
    }

    public bool IsLoading => _viewModel.State is { IsLoading: true } or { IsLoadingMore: true };

    public Task RefreshAsync()
    {
        return _viewModel.RefreshAsync();
    }



    private void OnRetryButtonClicked(object? sender = null, EventArgs? args = null)
    {
        _ = _viewModel.RefreshAsync();
    }

    private void OnScrollValueChanged(object? sender, EventArgs args)
    {
        if (_disposed || _vadjustment is null)
            return;

        var currentY = _vadjustment.Value;
        if (currentY + _vadjustment.PageSize >= _vadjustment.Upper - 240)
        {
            _ = _viewModel.LoadMoreAsync();
        }
    }

    private void OnStateChanged(object? sender, SearchViewState state)
    {
        Functions.IdleAdd(0, () =>
        {
            if (!_disposed)
            {
                RefreshLoadingChanged?.Invoke(this, state.IsLoading || state.IsLoadingMore);
                Render(state);
            }
            return false;
        });
    }

    private void Render(SearchViewState state)
    {

        if (state.IsLoading)
        {
            _loadingLabel.SetText(string.IsNullOrWhiteSpace(state.Summary) ? "Searching YouTube…" : state.Summary);
            _stack.VisibleChildName = "loading";
        }
        else if (state.Videos.Count == 0)
        {
            if (state.Summary.Contains("could not", StringComparison.OrdinalIgnoreCase) ||
                state.Summary.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                _errorPage.Description = state.Summary;
                _stack.VisibleChildName = "error";
            }
            else
            {
                _emptyPage.Description = string.IsNullOrWhiteSpace(state.Summary) || state.Summary == "Search complete."
                    ? "Try different keywords or check spelling."
                    : state.Summary;
                _stack.VisibleChildName = "empty";
            }
        }
        else
        {
            ApplyVideos(state.Videos);
            _paginationLoadingRevealer.RevealChild = state.IsLoadingMore;
            _stack.VisibleChildName = "content";
        }
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

        var card = new VideoCardView(_thumbnails, _watchProgress, _videoActions);
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

        if (_vadjustment is not null)
        {
            _vadjustment.OnValueChanged -= OnScrollValueChanged;
        }

        _viewModel.StateChanged -= OnStateChanged;

        _videoGrid.Factory = null;
        foreach (var association in _cardsByListItem)
            DisposeVideoCardCell(association.Key, association.Value);
        _cardsByListItem.Clear();

        _videoFactory.OnSetup -= OnVideoCardSetup;
        _videoFactory.OnBind -= OnVideoCardBind;
        _videoFactory.OnUnbind -= OnVideoCardUnbind;
        _videoFactory.OnTeardown -= OnVideoCardTeardown;

        _videoGrid.Dispose();
        _videoSelection.Dispose();
        _videoFactory.Dispose();
        _videoIds.Dispose();

        base.Dispose();
    }
}
