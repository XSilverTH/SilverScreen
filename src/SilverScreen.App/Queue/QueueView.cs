using Gtk;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Queue;
using XSTH.Blueprint.Helpers;

namespace SilverScreen.Queue;

public partial class QueueView : ViewBase<Box, QueueViewModel>
{
    private static readonly ILogger Logger = Log.ForContext<QueueView>();
    private readonly Action? _closeRequested;
    private readonly SignalListItemFactory _factory;
    private readonly StringList _itemIds;
    private readonly Dictionary<Guid, int> _itemIndicesById = [];
    private readonly Dictionary<string, QueueItem> _itemsById = [];
    private readonly Dictionary<Widget, QueueItemRowView> _rowsByCell = [];
    private readonly IThumbnailService _thumbnails;
    private readonly Action<int>? _trackJumpRequested;
    private readonly QueueViewModel _viewModel;
    private QueueItem[] _displayedItems = [];

    public QueueView(QueueViewModel viewModel, IThumbnailService thumbnails,
        Action? closeRequested = null, Action<int>? trackJumpRequested = null)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _thumbnails = thumbnails;
        _closeRequested = closeRequested;
        _trackJumpRequested = trackJumpRequested;

        _itemIds = Lifetime.Own(StringList.New([]));
        var selection = Lifetime.Own(NoSelection.New(_itemIds));
        _factory = Lifetime.Own(SignalListItemFactory.New());
        Lifetime.Track(() => _factory.OnSetup += OnRowSetup, () => _factory.OnSetup -= OnRowSetup);
        Lifetime.Track(() => _factory.OnBind += OnRowBind, () => _factory.OnBind -= OnRowBind);
        Lifetime.Track(() => _factory.OnUnbind += OnRowUnbind, () => _factory.OnUnbind -= OnRowUnbind);
        Lifetime.Track(() => _factory.OnTeardown += OnRowTeardown, () => _factory.OnTeardown -= OnRowTeardown);

        queue_list.Model = selection;
        queue_list.Factory = _factory;

        ViewModel = viewModel;
    }

    public event EventHandler? CloseRequested;

    protected override void BindViewModel(QueueViewModel viewModel, BindingScope<QueueViewModel> bindings)
    {
        bindings.Bind(
            nameof(QueueViewModel.State),
            this,
            static vm => vm.State,
            static (view, state) => view.Render(state));
    }

    private void OnCloseButtonClicked(object? sender, EventArgs args)
    {
        _closeRequested?.Invoke();
        CloseRequested?.Invoke(this, EventArgs.Empty);
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


    private void Render(QueuePresentationState state)
    {
        ApplyItems(state.Items);
        if (state.CurrentPlayingIndex >= 0 && state.CurrentPlayingIndex < state.Items.Count)
        {
            long remainingTicks = 0;
            for (var i = state.CurrentPlayingIndex; i < state.Items.Count; i++)
                remainingTicks += state.Items[i].Video.Duration.Ticks;
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
        {
            if (row.Item is not { } item)
                continue;

            var index = GetItemIndex(item.Id);
            if (index < 0)
                row.Unbind();
            else
                row.Bind(_displayedItems[index], index, _displayedItems.Length, _viewModel.State.CurrentPlayingIndex);
        }
    }

    private void ApplyItems(IReadOnlyList<QueueItem> items)
    {
        var nextItems = items as QueueItem[] ?? [.. items];
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

        var itemsChanged = removedMiddleCount != 0 || addedMiddleCount != 0;
        if (!itemsChanged)
            for (var i = 0; i < nextItems.Length; i++)
                if (!Equals(_displayedItems[i], nextItems[i]))
                {
                    itemsChanged = true;
                    break;
                }

        _displayedItems = nextItems;
        if (itemsChanged)
        {
            _itemsById.Clear();
            _itemIndicesById.Clear();
            for (var i = 0; i < nextItems.Length; i++)
            {
                var item = nextItems[i];
                _itemsById[item.Id.ToString()] = item;
                _itemIndicesById[item.Id] = i;
            }
        }

        if (removedMiddleCount == 0 && addedMiddleCount == 0)
            return;

        var addedIds = new string[addedMiddleCount];
        for (var i = 0; i < addedMiddleCount; i++)
            addedIds[i] = nextItems[prefixLength + i].Id.ToString();
        _itemIds.Splice((uint)prefixLength, (uint)removedMiddleCount, addedIds);
    }

    private void OnRowSetup(object? sender, SignalListItemFactory.SetupSignalArgs args)
    {
        if (args.Object is not ListItem listItem)
            return;

        listItem.Focusable = true;
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
        return _itemIndicesById.GetValueOrDefault(itemId, -1);
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
        var sourceIndex = GetItemIndex(itemId);
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
            _itemsById.Clear();
            _itemIndicesById.Clear();
            _viewModel.Dispose();
        }

        base.Dispose(disposing);
    }
}