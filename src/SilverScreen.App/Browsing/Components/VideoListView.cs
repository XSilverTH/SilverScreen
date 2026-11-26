using System.Runtime.CompilerServices;
using Adw;
using Gtk;
using Serilog;
using SilverScreen.Core.Browsing;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using SilverScreen.Player;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Browsing.Components;

public partial class VideoListView : ViewBase<Bin>
{
    private const int RowsPerBatch = VideoFeedConstants.RowsPerBatch;
    private const int CardWidthWithMargin = 330;
    private const int RefreshScrollStabilizationPasses = 3;
    private const uint RefreshScrollStabilizationMilliseconds = 16;
    private static readonly ILogger Logger = Log.ForContext<VideoListView>();
    private readonly ConditionalWeakTable<ListItem, VideoCardView> _cardsByListItem = new();
    private readonly VideoCardServices _services;
    private readonly IVideoListSource _source;
    private readonly SignalListItemFactory _videoFactory;
    private readonly StringList _videoIds;
    private readonly Dictionary<string, VideoSummary> _videosById = [];
    private Action? _currentStatusAction;
    private VideoSummary[] _displayedVideos = [];
    private int _measuredCardWidth;
    private Label? _paginationErrorLabel;
    private Revealer? _paginationErrorRevealer;
    private long _refreshGeneration;
    private int _refreshScrollPass;
    private bool _refreshScrollPending;
    private uint _refreshScrollSource;

    public VideoListView(
        IVideoListSource source,
        VideoCardServices services)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _services = services ?? throw new ArgumentNullException(nameof(services));

        Vadjustment = video_list_scrolled_window.Vadjustment;
        if (Vadjustment is not null)
            Lifetime.Track(
                () => Vadjustment.OnValueChanged += OnScrollValueChanged,
                () => Vadjustment.OnValueChanged -= OnScrollValueChanged);

        _videoIds = Lifetime.Own(StringList.New([]));
        var videoSelection = Lifetime.Own(NoSelection.New(_videoIds));
        _videoFactory = Lifetime.Own(SignalListItemFactory.New());
        Lifetime.Track(
            () =>
            {
                _videoFactory.OnSetup += OnVideoCardSetup;
                _videoFactory.OnBind += OnVideoCardBind;
                _videoFactory.OnUnbind += OnVideoCardUnbind;
                _videoFactory.OnTeardown += OnVideoCardTeardown;
            },
            () =>
            {
                _videoFactory.OnSetup -= OnVideoCardSetup;
                _videoFactory.OnBind -= OnVideoCardBind;
                _videoFactory.OnUnbind -= OnVideoCardUnbind;
                _videoFactory.OnTeardown -= OnVideoCardTeardown;
            });
        video_list_grid.Model = videoSelection;
        Lifetime.Track(
            () => video_list_grid.Factory = _videoFactory,
            () => video_list_grid.Factory = null);
        Lifetime.Own(_source);
        Lifetime.Track(
            () => _source.StateChanged += OnStateChanged,
            () => _source.StateChanged -= OnStateChanged);
        EnsurePaginationErrorFooter();
        Render(_source.State);
    }

    public ScrolledWindow ScrolledWindow => video_list_scrolled_window;

    public Adjustment? Vadjustment { get; }

    public bool IsLoading => _source.State.IsLoading || _source.State.IsLoadingMore;

    public event EventHandler<bool>? RefreshLoadingChanged;

    private int GetColumnsPerRow()
    {
        var width = video_list_grid.GetWidth();
        if (width <= 0)
        {
            var scrolledWidth = video_list_scrolled_window.GetWidth();
            if (scrolledWidth > 0)
                width = Math.Max(0, scrolledWidth - 48);
        }

        if (width <= 0)
        {
            var widgetWidth = Widget.GetWidth();
            if (widgetWidth > 0)
                width = Math.Max(0, widgetWidth - 48);
        }

        if (width <= 0)
            return VideoFeedConstants.DefaultColumns;

        var cardWidth = _measuredCardWidth > 0 ? _measuredCardWidth : CardWidthWithMargin;
        if (_measuredCardWidth <= 0)
            foreach (var card in _cardsByListItem)
            {
                card.Value.Widget.Measure(Orientation.Horizontal, -1, out _, out var nat, out _, out _);
                if (nat <= 0) continue;
                _measuredCardWidth = nat;
                cardWidth = nat;
                break;
            }

        var minColumns = (int)video_list_grid.MinColumns;
        if (minColumns <= 0) minColumns = 1;
        var maxColumns = (int)video_list_grid.MaxColumns;
        if (maxColumns <= 0) maxColumns = 8;

        var computed = width / Math.Max(1, cardWidth);
        return Math.Clamp(computed, minColumns, maxColumns);
    }

    public int GetBatchSize()
    {
        return GetColumnsPerRow() * RowsPerBatch;
    }

    private void ScrollToTop()
    {
        if (IsDisposed || Vadjustment is null)
            return;

        Vadjustment.SetValue(Vadjustment.Lower);
    }

    private void CancelRefreshScroll()
    {
        if (_refreshScrollSource == 0)
            return;

        Lifetime.Cancel(_refreshScrollSource);
        _refreshScrollSource = 0;
    }

    private void ScheduleRefreshScroll(long generation, bool resetPass = true)
    {
        CancelRefreshScroll();
        if (resetPass)
            _refreshScrollPass = 0;
        _refreshScrollPending = true;

        _refreshScrollSource = Lifetime.Timeout(
            RefreshScrollStabilizationMilliseconds,
            () =>
            {
                _refreshScrollSource = 0;
                if (IsDisposed || generation != _refreshGeneration)
                    return false;

                ScrollToTop();
                if (++_refreshScrollPass < RefreshScrollStabilizationPasses)
                {
                    ScheduleRefreshScroll(generation, false);
                    return false;
                }

                _refreshScrollPending = false;
                return false;
            });
    }

    public async Task RefreshAsync()
    {
        var generation = ++_refreshGeneration;
        _refreshScrollPending = true;
        CancelRefreshScroll();

        try
        {
            await _source.RefreshAsync(GetBatchSize()).ConfigureAwait(false);
        }
        finally
        {
            if (!IsDisposed && generation == _refreshGeneration)
                ScheduleRefreshScroll(generation);
        }
    }

    private void OnRetryButtonClicked(object? sender = null, EventArgs? args = null)
    {
        if (_currentStatusAction != null)
        {
            _currentStatusAction();
            return;
        }

        RefreshAsync().FireAndForget(Logger);
    }

    private void OnPaginationRetryClicked(object? sender, EventArgs args)
    {
        // The engine preserved the continuation token on a flaky page, so this
        // re-attempts the SAME page instead of restarting or stalling the feed.
        _source.LoadMoreAsync(GetBatchSize()).FireAndForget(Logger);
    }

    private void EnsurePaginationErrorFooter()
    {
        if (_paginationErrorRevealer is not null)
            return;

        var revealer = Revealer.New();
        revealer.Halign = Align.Center;
        revealer.Valign = Align.End;
        revealer.MarginBottom = 24;

        var box = Box.New(Orientation.Horizontal, 10);
        box.Halign = Align.Center;
        box.Valign = Align.Center;

        var label = Label.New(null);
        label.Wrap = true;
        label.MaxWidthChars = 60;
        label.Xalign = 0;
        box.Append(label);

        var retry = Button.NewWithLabel("Retry");
        Lifetime.Track(
            () => retry.OnClicked += OnPaginationRetryClicked,
            () => retry.OnClicked -= OnPaginationRetryClicked);
        box.Append(retry);

        revealer.Child = box;
        Lifetime.Track(
            () => video_list_content_overlay.AddOverlay(revealer),
            () => video_list_content_overlay.RemoveOverlay(revealer));
        _paginationErrorRevealer = revealer;
        _paginationErrorLabel = label;
    }

    private void UpdatePaginationErrorFooter(VideoListPresentationState state)
    {
        if (_paginationErrorRevealer is null || _paginationErrorLabel is null)
            return;

        var show = !string.IsNullOrWhiteSpace(state.PaginationError)
                   && !state.IsLoadingMore
                   && _displayedVideos.Length > 0;
        if (show)
            _paginationErrorLabel.SetText(state.PaginationError!);
        _paginationErrorRevealer.RevealChild = show;
    }

    private void OnScrollValueChanged(object? sender, EventArgs args)
    {
        if (IsDisposed || _refreshScrollPending || _source.State.IsLoading || _source.State.IsLoadingMore ||
            !_source.HasMore || Vadjustment is null ||
            Vadjustment.Value + Vadjustment.PageSize < Vadjustment.Upper - 240)
            return;

        _source.LoadMoreAsync(GetBatchSize()).FireAndForget(Logger);
    }

    private void OnStateChanged(object? sender, VideoListPresentationState state)
    {
        GtkUiDispatch.Run(() => !IsDisposed, () =>
        {
            RefreshLoadingChanged?.Invoke(this, state.IsLoading || state.IsLoadingMore);
            Render(state);
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

            UpdatePaginationErrorFooter(state);
            video_list_stack.VisibleChildName = "loading";
            return;
        }

        if (_displayedVideos.Length > 0)
        {
            video_list_pagination_loading_revealer.RevealChild = state.IsLoadingMore;
            video_list_pagination_loading_label.SetText(state.PaginationLoadingMessage);
            UpdatePaginationErrorFooter(state);
            video_list_stack.VisibleChildName = "content";
            return;
        }

        video_list_status_page.Title = state.Status.Title;
        video_list_status_page.Description = state.Status.Description;
        video_list_status_page.IconName = state.Status.IconName;
        _currentStatusAction = state.Status.Action;
        video_list_retry_button.Label = state.Status.ActionLabel ?? "Retry";
        video_list_retry_button.Visible = state.Status.ShowRetry || state.Status.Action != null ||
                                          !string.IsNullOrWhiteSpace(state.Status.ActionLabel);
        UpdatePaginationErrorFooter(state);
        video_list_stack.VisibleChildName = "status";
    }

    private void ApplyVideos(IReadOnlyList<VideoSummary> videos)
    {
        var nextVideos = videos as VideoSummary[] ?? [.. videos];
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
        _displayedVideos = nextVideos;
        if (removedMiddleCount == 0 && addedMiddleCount == 0)
            return;

        var isPaginationAppend = removedMiddleCount == 0 && prefixLength == _displayedVideos.Length - addedMiddleCount;
        var isInPlaceUpdate = removedMiddleCount > 0 && removedMiddleCount == addedMiddleCount;
        if (isInPlaceUpdate)
            for (var i = 0; i < removedMiddleCount; i++)
                if (_videoIds.GetString((uint)(prefixLength + i)) != nextVideos[prefixLength + i].Id)
                {
                    isInPlaceUpdate = false;
                    break;
                }

        if (isPaginationAppend)
        {
            for (var i = prefixLength; i < nextVideos.Length; i++)
                _videosById[nextVideos[i].Id] = nextVideos[i];
        }
        else
        {
            _videosById.Clear();
            foreach (var video in nextVideos)
                _videosById[video.Id] = video;
        }

        if (!isPaginationAppend && !isInPlaceUpdate)
        {
            if (!_refreshScrollPending)
            {
                var generation = ++_refreshGeneration;
                ScheduleRefreshScroll(generation);
            }

            ScrollToTop();
        }

        var addedMiddleIds = new string[addedMiddleCount];
        for (var i = 0; i < addedMiddleCount; i++)
            addedMiddleIds[i] = nextVideos[prefixLength + i].Id;
        _videoIds.Splice((uint)prefixLength, (uint)removedMiddleCount, addedMiddleIds);
    }

    private void OnVideoCardSetup(object? sender, SignalListItemFactory.SetupSignalArgs args)
    {
        if (args.Object is not ListItem listItem)
            return;

        var card = new VideoCardView(_services);
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelRefreshScroll();
            foreach (var association in _cardsByListItem)
                DisposeVideoCardCell(association.Key, association.Value);
            _cardsByListItem.Clear();

            RefreshLoadingChanged = null;
        }

        base.Dispose(disposing);
    }
}