using Gtk;
using Serilog;
using SilverScreen.Infrastructure.Common;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;
namespace SilverScreen.Player.Comments;

public partial class CommentsView : ViewBase<Box>
{
    private static readonly ILogger Logger = Log.ForContext<CommentsView>();

    private readonly Action _closeRequested;
    private readonly SignalListItemFactory _factory;
    private readonly StringList _itemIds;
    private readonly Dictionary<Widget, CommentRowView> _rowsByCell = [];
    private readonly NoSelection _selection;
    private readonly Adjustment? _vadjustment;
    private readonly CommentsViewModel _viewModel;
    private string[] _displayedCommentIds = [];
    private bool _disposed;
    private CommentsViewState _state;

    public CommentsView(CommentsViewModel viewModel, Action closeRequested)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _closeRequested = closeRequested ?? throw new ArgumentNullException(nameof(closeRequested));
        _state = _viewModel.State;
        _viewModel.StateChanged += OnViewModelStateChanged;

        _vadjustment = comments_scrolled_window.Vadjustment;
        if (_vadjustment is not null)
            _vadjustment.OnValueChanged += OnScrollValueChanged;

        _itemIds = StringList.New([]);
        _selection = NoSelection.New(_itemIds);
        _factory = SignalListItemFactory.New();
        _factory.OnSetup += OnRowSetup;
        _factory.OnBind += OnRowBind;
        _factory.OnUnbind += OnRowUnbind;
        _factory.OnTeardown += OnRowTeardown;

        comments_list.Model = _selection;
        comments_list.Factory = _factory;
        comments_stack.VisibleChildName = "unavailable";
    }
    public void SetVideo(string? videoId)
    {
        _viewModel.SetVideo(videoId);
    }

    public void EnsureLoaded()
    {
        _viewModel.EnsureLoaded();
    }

    private void OnCloseButtonClicked(object? sender, EventArgs args)
    {
        _closeRequested();
    }

    private void OnRetryButtonClicked(object? sender = null, EventArgs? args = null)
    {
        _viewModel.Refresh();
    }

    private void OnScrollValueChanged(object? sender, EventArgs args)
    {
        if (_disposed || _vadjustment is null ||
            _vadjustment.Value + _vadjustment.PageSize < _vadjustment.Upper - 280)
            return;

        _viewModel.LoadMoreAsync().FireAndForget(Logger);
    }

    private void OnSortDropdownNotify(object? sender, EventArgs args)
    {
        _viewModel.SetSortSelection(comments_sort_dropdown.GetSelected());
    }

    private void OnViewModelStateChanged(object? sender, CommentsViewState state)
    {
        Functions.IdleAdd(0, () =>
        {
            if (_disposed)
                return false;

            Render(state);
            return false;
        });
    }

    private void Render(CommentsViewState state)
    {
        _state = state;
        ApplyComments(state.VisibleComments);

        comments_pagination_loading_revealer.RevealChild = state.IsLoadingMore;
        comments_pagination_loading_label.SetText(state.PaginationLoadingMessage);

        // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
        switch (state.Status)
        {
            case CommentsViewStatus.Unavailable:
                comments_stack.VisibleChildName = "unavailable";
                break;
            case CommentsViewStatus.Loading:
                comments_stack.VisibleChildName = "loading";
                break;
            case CommentsViewStatus.Error:
                comments_error_page.Description = state.StatusMessage;
                comments_stack.VisibleChildName = "error";
                break;
            case CommentsViewStatus.Empty:
                comments_empty_page.Description = state.StatusMessage;
                comments_stack.VisibleChildName = "empty";
                break;
            case CommentsViewStatus.List:
                comments_stack.VisibleChildName = "list";
                break;
        }
    }

    private void ApplyComments(IReadOnlyList<CommentRowState> visibleComments)
    {
        var nextIds = visibleComments.Select(row => row.Comment.Id).ToArray();
        var prefixLength = 0;
        while (prefixLength < _displayedCommentIds.Length && prefixLength < nextIds.Length &&
               string.Equals(_displayedCommentIds[prefixLength], nextIds[prefixLength], StringComparison.Ordinal))
            prefixLength++;

        var suffixLength = 0;
        while (_displayedCommentIds.Length - suffixLength > prefixLength &&
               nextIds.Length - suffixLength > prefixLength &&
               string.Equals(_displayedCommentIds[_displayedCommentIds.Length - suffixLength - 1],
                   nextIds[nextIds.Length - suffixLength - 1], StringComparison.Ordinal))
            suffixLength++;

        var removedMiddleCount = _displayedCommentIds.Length - prefixLength - suffixLength;
        var addedMiddleCount = nextIds.Length - prefixLength - suffixLength;

        _displayedCommentIds = nextIds;
        if (removedMiddleCount == 0 && addedMiddleCount == 0)
            return;

        var addedMiddleIds = nextIds.Skip(prefixLength).Take(addedMiddleCount).ToArray();
        _itemIds.Splice((uint)prefixLength, (uint)removedMiddleCount, addedMiddleIds);
    }

    private void OnRowSetup(object? sender, SignalListItemFactory.SetupSignalArgs args)
    {
        if (args.Object is not ListItem listItem)
            return;

        var row = new CommentRowView(_viewModel.ToggleReplies);
        listItem.Child = row.Widget;
        _rowsByCell[row.Widget] = row;
    }

    private void OnRowBind(object? sender, SignalListItemFactory.BindSignalArgs args)
    {
        if (args.Object is not ListItem { Child: { } child, Item: StringObject { String: { } id } } ||
            !_rowsByCell.TryGetValue(child, out var row))
            return;

        var comment = _state.VisibleComments.FirstOrDefault(item =>
            string.Equals(item.Comment.Id, id, StringComparison.Ordinal));
        if (comment is null)
            return;

        row.Bind(comment.Comment, comment.ReplyCount, comment.RepliesExpanded);
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

    public new void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _viewModel.StateChanged -= OnViewModelStateChanged;
        if (_vadjustment is not null)
            _vadjustment.OnValueChanged -= OnScrollValueChanged;

        _viewModel.Dispose();
        _factory.OnSetup -= OnRowSetup;
        _factory.OnBind -= OnRowBind;
        _factory.OnUnbind -= OnRowUnbind;
        _factory.OnTeardown -= OnRowTeardown;
        foreach (var row in _rowsByCell.Values)
            row.Dispose();

        _rowsByCell.Clear();
        comments_list.Dispose();
        _selection.Dispose();
        _factory.Dispose();
        _itemIds.Dispose();
        base.Dispose();
    }
}