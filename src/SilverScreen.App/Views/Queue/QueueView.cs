using Adw;
using Gtk;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.ViewModels;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;
using Spinner = Gtk.Spinner;

namespace SilverScreen.Views.Queue;
public partial class QueueView : ViewBase<Box>
{
    private static readonly ILogger Logger = Log.ForContext<QueueView>();
    private readonly Action _closeRequested;
    private readonly StatusPage _emptyPage;
    private readonly SignalListItemFactory _factory;
    private readonly Box _footer;
    private readonly StringList _itemIds;
    private readonly Dictionary<string, QueueItem> _itemsById = [];
    private readonly ListView _list;
    private readonly Button _playButton;
    private readonly Spinner _playSpinner;
    private readonly Stack _playStack;
    private readonly Dictionary<Widget, QueueItemRowView> _rowsByCell = [];
    private readonly ScrolledWindow _scrolledWindow;
    private readonly NoSelection _selection;
    private readonly Label _summary;
    private readonly IThumbnailService _thumbnails;
    private readonly IWatchProgressService _watchProgress;
    private readonly QueueViewModel _viewModel;
    private QueueItem[] _displayedItems = [];
    private bool _disposed;

    private readonly Action<int>? _trackJumpRequested;

    public QueueView(QueueViewModel viewModel, IThumbnailService thumbnails, IWatchProgressService watchProgress,
        Action closeRequested, Action<int>? trackJumpRequested = null)
    {
        _viewModel = viewModel;
        _thumbnails = thumbnails;
        _watchProgress = watchProgress;
        _closeRequested = closeRequested;
        _trackJumpRequested = trackJumpRequested;
        GetRequiredObject<Button>("queue_clear_button");
        _emptyPage = GetRequiredObject<StatusPage>("queue_empty_page");
        _footer = GetRequiredObject<Box>("queue_footer");
        _playButton = GetRequiredObject<Button>("queue_play_button");
        _playSpinner = GetRequiredObject<Spinner>("queue_play_spinner");
        _playStack = GetRequiredObject<Stack>("queue_play_stack");
        _scrolledWindow = GetRequiredObject<ScrolledWindow>("queue_scrolled_window");
        _summary = GetRequiredObject<Label>("queue_summary_label");

        _itemIds = StringList.New([]);
        _selection = NoSelection.New(_itemIds);
        _factory = SignalListItemFactory.New();
        _factory.OnSetup += OnRowSetup;
        _factory.OnBind += OnRowBind;
        _factory.OnUnbind += OnRowUnbind;
        _factory.OnTeardown += OnRowTeardown;
        _list = GetRequiredObject<ListView>("queue_list");
        _list.Model = _selection;
        _list.Factory = _factory;


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
            _summary.SetText($"Playing {state.CurrentPlayingIndex + 1} of {state.Items.Count} · {FormatDuration(remainingDuration)} remaining");
        }
        else
        {
            _summary.SetText(FormatSummary(state.Items.Count, state.TotalDuration));
        }
        _emptyPage.Visible = !state.IsVisible;
        _scrolledWindow.Visible = state.IsVisible;
        _footer.Visible = state.IsVisible && _trackJumpRequested is null;
        _playButton.Sensitive = state.CanPlay;
        _playStack.VisibleChildName = state.IsLaunching ? "launching" : "idle";
        _playSpinner.Spinning = state.IsLaunching;
        RefreshVisibleRows();
    }

    private void RefreshVisibleRows()
    {
        foreach (var row in _rowsByCell.Values)
        {
            if (row.Item is { } item)
            {
                row.Bind(item, GetItemIndex(item.Id), _displayedItems.Length, _viewModel.State.CurrentPlayingIndex);
            }
        }
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

        var row = new QueueItemRowView(_thumbnails, _watchProgress, _viewModel.Move, RequestDrop, _viewModel.Remove, OnRowPlayRequested);
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
        _list.Dispose();
        _selection.Dispose();
        _factory.Dispose();
        _itemIds.Dispose();
        _viewModel.Dispose();
        base.Dispose();
    }
}