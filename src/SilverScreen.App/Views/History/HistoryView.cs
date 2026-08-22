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

namespace SilverScreen.Views.History;
public partial class HistoryView : ViewBase<Box>
{
    private static readonly ILogger Logger = Log.ForContext<HistoryView>();
    private readonly ConditionalWeakTable<ListItem, VideoCardView> _cardsByListItem = new();
    private readonly Stack _stack;
    private readonly ScrolledWindow _scrolledWindow;
    private readonly Adjustment? _vadjustment;
    private readonly GridView _videoGrid;
    private readonly StatusPage _signedOutPage;
    private readonly StatusPage _emptyPage;
    private readonly StatusPage _errorPage;
    private readonly Button _retryButton;
    private readonly Revealer _paginationLoadingRevealer;
    private readonly Label _paginationLoadingLabel;
    private readonly IThumbnailService _thumbnails;
    private readonly IWatchProgressService _watchProgress;
    private readonly VideoCardActions _videoActions;
    private readonly SignalListItemFactory _videoFactory;
    private readonly StringList _videoIds;
    private readonly NoSelection _videoSelection;
    private readonly Dictionary<string, VideoSummary> _videosById = [];
    private readonly HistoryViewModel _viewModel;
    private VideoSummary[] _displayedVideos = [];
    private bool _disposed;

    public HistoryView(
        HistoryViewModel viewModel,
        IThumbnailService thumbnails,
        IWatchProgressService watchProgress,
        VideoCardActions videoActions)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
        _watchProgress = watchProgress ?? throw new ArgumentNullException(nameof(watchProgress));
        _videoActions = videoActions ?? throw new ArgumentNullException(nameof(videoActions));

        _stack = GetRequiredObject<Stack>("history_stack");
        _scrolledWindow = GetRequiredObject<ScrolledWindow>("history_scrolled_window");
        _videoGrid = GetRequiredObject<GridView>("history_video_grid");
        _signedOutPage = GetRequiredObject<StatusPage>("history_signed_out_page");
        _emptyPage = GetRequiredObject<StatusPage>("history_empty_page");
        _errorPage = GetRequiredObject<StatusPage>("history_error_page");
        _retryButton = GetRequiredObject<Button>("history_retry_button");
        _paginationLoadingRevealer = GetRequiredObject<Revealer>("history_pagination_loading_revealer");
        _paginationLoadingLabel = GetRequiredObject<Label>("history_pagination_loading_label");

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
        _videoGrid.Model = _videoSelection;
        _videoGrid.Factory = _videoFactory;

        _viewModel.StateChanged += OnStateChanged;
        Render(_viewModel.State);
    }

    public bool IsLoading => _viewModel.State.IsLoading || _viewModel.State.IsLoadingMore;

    public event EventHandler<bool>? RefreshLoadingChanged;

    public Task RefreshAsync()
    {
        return _viewModel.RefreshAsync();
    }

    private void OnRetryButtonClicked(object? sender = null, EventArgs? args = null)
    {
        _viewModel.RefreshAsync().FireAndForget(Logger);
    }

    private void OnScrollValueChanged(object? sender, EventArgs args)
    {
        if (_disposed || _vadjustment is null ||
            _vadjustment.Value + _vadjustment.PageSize < _vadjustment.Upper - 240)
            return;

        if (_viewModel.State is { IsLoading: false, IsLoadingMore: false, HasMore: true })
            _viewModel.LoadMoreAsync().FireAndForget(Logger);
    }

    private void OnStateChanged(object? sender, HistoryViewState state)
    {
        Functions.IdleAdd(0, () =>
        {
            if (_disposed)
                return false;

            RefreshLoadingChanged?.Invoke(this, state.IsLoading || state.IsLoadingMore);
            Render(state);
            return false;
        });
    }

    private void Render(HistoryViewState state)
    {
        ApplyVideos(state.Videos);

        if (state.IsLoading && _displayedVideos.Length == 0)
        {
            _stack.VisibleChildName = "loading";
            return;
        }

        if (_displayedVideos.Length > 0)
        {
            _stack.VisibleChildName = "content";
            _paginationLoadingRevealer.RevealChild = state.IsLoadingMore;
            _paginationLoadingLabel.SetText("Loading more history…");
            return;
        }

        switch (state.Status)
        {
            case AuthenticatedHistoryStatus.AuthenticationRequired:
            case AuthenticatedHistoryStatus.AuthenticationRejected:
                if (!string.IsNullOrWhiteSpace(state.Summary))
                    _signedOutPage.Description = state.Summary;
                else
                    _signedOutPage.Description = "Watch history requires an active YouTube session.";
                _stack.VisibleChildName = "signed_out";
                break;

            case AuthenticatedHistoryStatus.TemporaryBackendFailure:
                if (!string.IsNullOrWhiteSpace(state.Summary))
                    _errorPage.Description = state.Summary;
                else
                    _errorPage.Description = "Failed to load your watch history. Check your network connection and try again.";
                _stack.VisibleChildName = "error";
                break;

            case AuthenticatedHistoryStatus.Empty:
            case AuthenticatedHistoryStatus.Success:
            default:
                if (!string.IsNullOrWhiteSpace(state.Summary))
                    _emptyPage.Description = state.Summary;
                else
                    _emptyPage.Description = "Videos you watch on YouTube will appear here.";
                _stack.VisibleChildName = "empty";
                break;
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
        _viewModel.StateChanged -= OnStateChanged;

        if (_vadjustment is not null)
            _vadjustment.OnValueChanged -= OnScrollValueChanged;

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
        base.Dispose();
    }
}
