using System.Runtime.CompilerServices;
using Adw;
using Gtk;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure;
using SilverScreen.ViewModels;
using SilverScreen.Views.Components;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;

namespace SilverScreen.Views.Home;

public class HomeView : ViewBase<Box>
{
    private static readonly ILogger Logger = Log.ForContext<HomeView>();
    private readonly ConditionalWeakTable<ListItem, VideoCardView> _cardsByListItem = new();
    private readonly Overlay _contentOverlay;
    private readonly Label _paginationLoadingLabel;
    private readonly Revealer _paginationLoadingRevealer;
    private readonly ScrolledWindow _scrolledWindow;
    private readonly Box _statusHost;
    private readonly Box _statusLoadingPage;
    private readonly StatusPage _statusPage;
    private readonly IThumbnailService _thumbnails;
    private readonly Adjustment? _vadjustment;
    private readonly VideoCardActions _videoActions;
    private readonly SignalListItemFactory _videoFactory;
    private readonly GridView _videoGrid;
    private readonly StringList _videoIds;
    private readonly NoSelection _videoSelection;
    private readonly Dictionary<string, VideoSummary> _videosById = [];
    private readonly HomeViewModel _viewModel;
    private readonly IWatchProgressService _watchProgress;
    private VideoSummary[] _displayedVideos = [];
    private bool _disposed;

    public HomeView(
        HomeViewModel viewModel,
        IThumbnailService thumbnails,
        IWatchProgressService watchProgress,
        VideoCardActions videoActions)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
        _watchProgress = watchProgress ?? throw new ArgumentNullException(nameof(watchProgress));
        _videoActions = videoActions ?? throw new ArgumentNullException(nameof(videoActions));

        _statusHost = GetRequiredObject<Box>("home_status_host");
        _statusLoadingPage = GetRequiredObject<Box>("home_status_loading_page");
        _statusPage = GetRequiredObject<StatusPage>("home_status_page");
        _contentOverlay = GetRequiredObject<Overlay>("home_content_overlay");
        _scrolledWindow = GetRequiredObject<ScrolledWindow>("home_scrolled_window");
        _paginationLoadingRevealer = GetRequiredObject<Revealer>("home_pagination_loading_revealer");
        _paginationLoadingLabel = GetRequiredObject<Label>("home_pagination_loading_label");
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

    private void OnScrollValueChanged(object? sender, EventArgs args)
    {
        if (_disposed || _vadjustment is null ||
            _vadjustment.Value + _vadjustment.PageSize < _vadjustment.Upper - 240)
            return;

        _viewModel.LoadMoreAsync().FireAndForget(Logger);
    }

    private void OnStateChanged(object? sender, HomeFeedState state)
    {
        Functions.IdleAdd(0, () =>
        {
            if (_disposed) return false;
            RefreshLoadingChanged?.Invoke(this, state.IsLoading || state.IsLoadingMore);
            Render(state);

            return false;
        });
    }

    private void Render(HomeFeedState state)
    {
        ApplyVideos(state.Videos);

        var hasDisplayedVideos = _displayedVideos.Length > 0;
        _statusHost.Visible = false;
        _contentOverlay.Visible = false;

        if (!hasDisplayedVideos)
        {
            ShowStatus(state);
            return;
        }

        _contentOverlay.Visible = true;
        _paginationLoadingRevealer.RevealChild = state.IsLoadingMore;
        _paginationLoadingLabel.SetText("Loading more videos…");
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
        _viewModel.Dispose();
        base.Dispose();
    }
}