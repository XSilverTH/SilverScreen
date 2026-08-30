using Gtk;
using Serilog;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Queue;
using SilverScreen.Infrastructure.Common;
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
    private bool _disposed;

    public QueueView(QueueViewModel viewModel, IThumbnailService thumbnails,
        Action closeRequested, Action<int>? trackJumpRequested = null)
    {
        _viewModel = viewModel;
        _thumbnails = thumbnails;
        _closeRequested = closeRequested;
        _trackJumpRequested = trackJumpRequested;

        _itemIds = StringList.New([]);
        _selection = NoSelection.New(_itemIds);
        _factory = SignalListItemFactory.New();
        _factory.OnSetup += OnRowSetup;
        _factory.OnBind += OnRowBind;
        _factory.OnUnbind += OnRowUnbind;
        _factory.OnTeardown += OnRowTeardown;

        queue_list.Model = _selection;
        queue_list.Factory = _factory;


        _viewModel.StateChanged += OnStateChanged;
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

    private void OnPlayButtonClicked(object? sender, EventArgs args)
    {
        _viewModel.PlayAllAsync().FireAndForget(Logger);
    }

    private void OnStateChanged(object? sender, QueuePresentationState state)
    {
        Functions.IdleAdd(0, () =>
        {
            if (!_disposed)
                Render(state);

            return false;
        });
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
            _viewModel.PlayAllAsync().FireAndForget(Logger);
        }
    }

    private void OnRowUnbind(object? sender, SignalListItemFactory.UnbindSignalArgs args)
    {
        if (args.Object is ListItem { Child: { } child } && _rowsByCell.TryGetValue(child, out var row))
            row.Unbind();
    }

    private void OnRowTeardown(object? sender, SignalListItemFactory.TeardownSignalArgs args)
    {
        if (args.Object is not ListItem { Child: { } child } || !_rowsByCell.Remove(child, out var row))
            return;

        row.Unbind();
        row.Dispose();
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

    public new void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _viewModel.StateChanged -= OnStateChanged;
        _factory.OnSetup -= OnRowSetup;
        _factory.OnBind -= OnRowBind;
        _factory.OnUnbind -= OnRowUnbind;
        _factory.OnTeardown -= OnRowTeardown;
        foreach (var row in _rowsByCell.Values)
            row.Dispose();

        _rowsByCell.Clear();
        queue_list.Dispose();
        _selection.Dispose();
        _factory.Dispose();
        _itemIds.Dispose();
        _viewModel.Dispose();
        base.Dispose();
    }
}