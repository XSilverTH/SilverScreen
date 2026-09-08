using Gtk;
using Serilog;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Common;
using SilverScreen.Core.Queue;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;

namespace SilverScreen.Queue;

public partial class QueueView : ViewBase<Box>
{
    private static readonly ILogger Logger = Log.ForContext<QueueView>();
    private readonly Action _closeRequested;
    private readonly SignalListItemFactory _factory;
    private readonly StringList _itemIds;
    private readonly Dictionary<string, QueueItem> _itemsById = [];
    private readonly Dictionary<Widget, QueueItemRowView> _rowsByCell = [];
    private readonly NoSelection _selection;
    private readonly IThumbnailService _thumbnails;
    private readonly Action<int>? _trackJumpRequested;
    private readonly QueueViewModel _viewModel;
    private QueueItem[] _displayedItems = [];

    public QueueView(QueueViewModel viewModel, IThumbnailService thumbnails,
        Action closeRequested, Action<int>? trackJumpRequested = null)
    {
        _viewModel = viewModel;
        _thumbnails = thumbnails;
        _closeRequested = closeRequested;
        _trackJumpRequested = trackJumpRequested;

        _itemIds = Lifetime.Own(StringList.New([]));
        _selection = Lifetime.Own(NoSelection.New(_itemIds));
        _factory = Lifetime.Own(SignalListItemFactory.New());
        Lifetime.Track(() => _factory.OnSetup += OnRowSetup, () => _factory.OnSetup -= OnRowSetup);
        Lifetime.Track(() => _factory.OnBind += OnRowBind, () => _factory.OnBind -= OnRowBind);
        Lifetime.Track(() => _factory.OnUnbind += OnRowUnbind, () => _factory.OnUnbind -= OnRowUnbind);
        Lifetime.Track(() => _factory.OnTeardown += OnRowTeardown, () => _factory.OnTeardown -= OnRowTeardown);

        queue_list.Model = _selection;
        queue_list.Factory = _factory;

        Lifetime.Track(() => _viewModel.StateChanged += OnStateChanged, () => _viewModel.StateChanged -= OnStateChanged);
        Render(_viewModel.State);
    }

    private void OnCloseButtonClicked(object? sender, EventArgs args)
    {
        _closeRequested();
    }

    private void OnClearButtonClicked(object? sender, EventArgs args)
    {
        _viewModel.Clear();
    }

    public event EventHandler<string>? PlayFailed;

    private void OnPlayButtonClicked(object? sender, EventArgs args)
    {
        PlayAllAndReportAsync().FireAndForget(Logger);
    }

    private async Task PlayAllAndReportAsync()
    {
        var error = await _viewModel.PlayAllAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(error))
            return;

        PlayFailed?.Invoke(this, error);
    }

    private void OnStateChanged(object? sender, QueuePresentationState state)
    {
        if (IsDisposed) return;
        try
        {
            Lifetime.Idle(() =>
            {
                if (!IsDisposed)
                    Render(state);

                return false;
            });
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void Render(QueuePresentationState state)
    {
        ApplyItems(state.Items);
        if (state.CurrentPlayingIndex >= 0 && state.CurrentPlayingIndex < state.Items.Count)
        {
            var remainingTicks = state.Items.Skip(state.CurrentPlayingIndex).Sum(item => item.Video.Duration.Ticks);
            var remainingDuration = TimeSpan.FromTicks(remainingTicks);
            queue_summary_label.SetText(
                $"Playing {state.CurrentPlayingIndex + 1} of {state.Items.Count} · {FormatDuration(remainingDuration)} remaining");
        }
        else
        {
            queue_summary_label.SetText(FormatSummary(state.Items.Count, state.TotalDuration));
        }

        queue_empty_page.Visible = !state.IsVisible;
        queue_scrolled_window.Visible = state.IsVisible;
        queue_footer.Visible = state.IsVisible && _trackJumpRequested is null;
        queue_play_button.Sensitive = state.CanPlay;
        queue_play_stack.VisibleChildName = state.IsLaunching ? "launching" : "idle";
        queue_play_spinner.Spinning = state.IsLaunching;
        RefreshVisibleRows();
    }

    private void RefreshVisibleRows()
    {
        foreach (var row in _rowsByCell.Values)
            if (row.Item is { } item)
                row.Bind(item, GetItemIndex(item.Id), _displayedItems.Length, _viewModel.State.CurrentPlayingIndex);
    }

    private void ApplyItems(IReadOnlyList<QueueItem> items)
    {
        var nextItems = items.ToArray();
        var prefixLength = 0;
        while (prefixLength < _displayedItems.Length && prefixLength < nextItems.Length &&
               _displayedItems[prefixLength].Id == nextItems[prefixLength].Id)
            prefixLength++;

        var suffixLength = 0;
        while (_displayedItems.Length - suffixLength > prefixLength &&
               nextItems.Length - suffixLength > prefixLength &&
               _displayedItems[_displayedItems.Length - suffixLength - 1].Id ==
               nextItems[nextItems.Length - suffixLength - 1].Id)
            suffixLength++;

        var removedMiddleCount = _displayedItems.Length - prefixLength - suffixLength;
        var addedMiddleCount = nextItems.Length - prefixLength - suffixLength;
        _itemsById.Clear();
        foreach (var item in nextItems)
            _itemsById[item.Id.ToString()] = item;

        _displayedItems = nextItems;
        if (removedMiddleCount == 0 && addedMiddleCount == 0)
            return;

        var addedIds = nextItems.Skip(prefixLength).Take(addedMiddleCount).Select(item => item.Id.ToString()).ToArray();
        _itemIds.Splice((uint)prefixLength, (uint)removedMiddleCount, addedIds);
    }

    private void OnRowSetup(object? sender, SignalListItemFactory.SetupSignalArgs args)
    {
        if (args.Object is not ListItem listItem)
            return;

        var row = new QueueItemRowView(_thumbnails, _viewModel.Move, RequestDrop, _viewModel.Remove,
            OnRowPlayRequested);
        listItem.Child = row.Widget;
        _rowsByCell[row.Widget] = row;
    }

    private void OnRowBind(object? sender, SignalListItemFactory.BindSignalArgs args)
    {
        if (args.Object is not ListItem { Child: { } child, Item: StringObject { String: { } id } } ||
            !_rowsByCell.TryGetValue(child, out var row) ||
            !_itemsById.TryGetValue(id, out var item))
            return;

        row.Bind(item, GetItemIndex(item.Id), _displayedItems.Length, _viewModel.State.CurrentPlayingIndex);
    }

    private int GetItemIndex(Guid itemId)
    {
        for (var index = 0; index < _displayedItems.Length; index++)
            if (_displayedItems[index].Id == itemId)
                return index;

        return -1;
    }

    private void OnRowPlayRequested(Guid itemId, int index)
    {
        if (_trackJumpRequested is not null)
        {
            _trackJumpRequested(index);
        }
        else
        {
            _viewModel.Move(itemId, 0);
            PlayAllAndReportAsync().FireAndForget(Logger);
        }
    }

    private void OnRowUnbind(object? sender, SignalListItemFactory.UnbindSignalArgs args)
    {
        if (args.Object is ListItem { Child: { } child } && _rowsByCell.TryGetValue(child, out var row))
            row.Unbind();
    }

    private void OnRowTeardown(object? sender, SignalListItemFactory.TeardownSignalArgs args)
    {
        if (args.Object is not ListItem listItem)
            return;

        if (listItem.Child is { } child && _rowsByCell.Remove(child, out var row))
        {
            row.Unbind();
            row.Dispose();
        }

        listItem.Child = null;
    }

    private void RequestDrop(Guid itemId, int insertionIndex)
    {
        var sourceIndex = _displayedItems.ToList().FindIndex(item => item.Id == itemId);
        if (sourceIndex < 0)
            return;

        var destinationIndex = insertionIndex;
        if (sourceIndex < insertionIndex)
            destinationIndex--;

        _viewModel.Move(itemId, destinationIndex);
    }

    private static string FormatSummary(int count, TimeSpan duration)
    {
        var noun = count == 1 ? "video" : "videos";
        return $"{count} {noun} · {FormatDuration(duration)}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes:00}m"
            : duration.TotalMinutes >= 1
                ? $"{(int)duration.TotalMinutes}m"
                : $"{duration.Seconds}s";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var row in _rowsByCell.Values)
                row.Dispose();

            _rowsByCell.Clear();
            _viewModel.Dispose();
        }

        base.Dispose(disposing);
    }
}