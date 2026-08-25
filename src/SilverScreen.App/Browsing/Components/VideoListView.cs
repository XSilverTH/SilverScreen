using System.Runtime.CompilerServices;
using Adw;
using Gtk;
using Serilog;
using SilverScreen.Browsing.Channel;
using SilverScreen.Browsing.History;
using SilverScreen.Browsing.Home;
using SilverScreen.Browsing.Search;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Player;
using SilverScreen.Infrastructure.Common;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;

namespace SilverScreen.Browsing.Components;

public partial class VideoListView : ViewBase<Bin>
{
    private static readonly ILogger Logger = Log.ForContext<VideoListView>();

    private readonly ConditionalWeakTable<ListItem, VideoCardView> _cardsByListItem = new();
    private readonly IVideoListSource _source;
    private readonly IThumbnailService _thumbnails;
    private readonly VideoCardActions _videoActions;
    private readonly SignalListItemFactory _videoFactory;
    private readonly StringList _videoIds;
    private readonly NoSelection _videoSelection;
    private readonly Dictionary<string, VideoSummary> _videosById = [];
    private readonly IWatchProgressService _watchProgress;
    private VideoSummary[] _displayedVideos = [];
    private bool _disposed;

    private VideoListView(
        IVideoListSource source,
        IThumbnailService thumbnails,
        IWatchProgressService watchProgress,
        VideoCardActions videoActions)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
        _watchProgress = watchProgress ?? throw new ArgumentNullException(nameof(watchProgress));
        _videoActions = videoActions ?? throw new ArgumentNullException(nameof(videoActions));

        Vadjustment = video_list_scrolled_window.Vadjustment;
        if (Vadjustment is not null)
            Vadjustment.OnValueChanged += OnScrollValueChanged;

        _videoIds = StringList.New([]);
        _videoSelection = NoSelection.New(_videoIds);
        _videoFactory = SignalListItemFactory.New();
        _videoFactory.OnSetup += OnVideoCardSetup;
        _videoFactory.OnBind += OnVideoCardBind;
        _videoFactory.OnUnbind += OnVideoCardUnbind;
        _videoFactory.OnTeardown += OnVideoCardTeardown;
        video_list_grid.Model = _videoSelection;
        video_list_grid.Factory = _videoFactory;

        _source.StateChanged += OnStateChanged;
        Render(_source.State);
    }

    public VideoListView(
        HomeFeedCoordinator coordinator,
        IThumbnailService thumbnails,
        IWatchProgressService watchProgress,
        VideoCardActions videoActions)
        : this(new HomeVideoListSource(coordinator), thumbnails, watchProgress, videoActions)
    {
    }

    public VideoListView(
        SearchViewModel viewModel,
        IThumbnailService thumbnails,
        IWatchProgressService watchProgress,
        VideoCardActions videoActions)
        : this(new SearchVideoListSource(viewModel), thumbnails, watchProgress, videoActions)
    {
    }

    public VideoListView(
        HistoryViewModel viewModel,
        IThumbnailService thumbnails,
        IWatchProgressService watchProgress,
        VideoCardActions videoActions)
        : this(new HistoryVideoListSource(viewModel), thumbnails, watchProgress, videoActions)
    {
    }

    public VideoListView(
        ChannelViewModel viewModel,
        IThumbnailService thumbnails,
        IWatchProgressService watchProgress,
        VideoCardActions videoActions)
        : this(new ChannelVideoListSource(viewModel), thumbnails, watchProgress, videoActions)
    {
    }

    public ScrolledWindow ScrolledWindow => video_list_scrolled_window;

    public Adjustment? Vadjustment { get; }

    public bool IsLoading => _source.State.IsLoading || _source.State.IsLoadingMore;

    public event EventHandler<bool>? RefreshLoadingChanged;

    public Task RefreshAsync()
    {
        return _source.RefreshAsync();
    }

    private void OnRetryButtonClicked(object? sender = null, EventArgs? args = null)
    {
        _source.RefreshAsync().FireAndForget(Logger);
    }

    private void OnScrollValueChanged(object? sender, EventArgs args)
    {
        if (_disposed || Vadjustment is null ||
            Vadjustment.Value + Vadjustment.PageSize < Vadjustment.Upper - 240)
            return;

        _source.LoadMoreAsync().FireAndForget(Logger);
    }

    private void OnStateChanged(object? sender, VideoListPresentationState state)
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

    private void Render(VideoListPresentationState state)
    {
        ApplyVideos(state.Videos);

        if (state.IsLoading && _displayedVideos.Length == 0)
        {
            if (string.IsNullOrWhiteSpace(state.LoadingMessage))
            {
                video_list_loading_label.Visible = false;
            }
            else
            {
                video_list_loading_label.SetText(state.LoadingMessage);
                video_list_loading_label.Visible = true;
            }

            video_list_stack.VisibleChildName = "loading";
            return;
        }

        if (_displayedVideos.Length > 0)
        {
            video_list_pagination_loading_revealer.RevealChild = state.IsLoadingMore;
            video_list_pagination_loading_label.SetText(state.PaginationLoadingMessage);
            video_list_stack.VisibleChildName = "content";
            return;
        }

        video_list_status_page.Title = state.Status.Title;
        video_list_status_page.Description = state.Status.Description;
        video_list_status_page.IconName = state.Status.IconName;
        video_list_retry_button.Visible = state.Status.ShowRetry;
        video_list_stack.VisibleChildName = "status";
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
        _source.StateChanged -= OnStateChanged;
        if (Vadjustment is not null)
            Vadjustment.OnValueChanged -= OnScrollValueChanged;

        video_list_grid.Factory = null;
        foreach (var association in _cardsByListItem)
            DisposeVideoCardCell(association.Key, association.Value);
        _cardsByListItem.Clear();

        _videoFactory.OnSetup -= OnVideoCardSetup;
        _videoFactory.OnBind -= OnVideoCardBind;
        _videoFactory.OnUnbind -= OnVideoCardUnbind;
        _videoFactory.OnTeardown -= OnVideoCardTeardown;

        video_list_scrolled_window.Child = null;
        video_list_grid.Dispose();
        _videoSelection.Dispose();
        _videoFactory.Dispose();
        _videoIds.Dispose();
        _source.Dispose();

        base.Dispose();
    }
}