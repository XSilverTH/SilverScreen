using Adw;
using Gtk;
using Serilog;
using SilverScreen.ViewModels;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;

namespace SilverScreen.Views.Comments;

public partial class CommentsView : ViewBase<Box>
{
    private static readonly ILogger Logger = Log.ForContext<CommentsView>();
    private readonly Action _closeRequested;
    private readonly StatusPage _emptyPage;
    private readonly StatusPage _errorPage;
    private readonly SignalListItemFactory _factory;
    private readonly StringList _itemIds;
    private readonly ListView _list;
    private readonly Dictionary<Widget, CommentRowView> _rowsByCell = [];
    private readonly NoSelection _selection;
    private readonly DropDown _sortDropdown;
    private readonly Stack _stack;
    private readonly CommentsViewModel _viewModel;
    private bool _disposed;
    private CommentsViewState _state;

    public CommentsView(CommentsViewModel viewModel, Action closeRequested)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _closeRequested = closeRequested ?? throw new ArgumentNullException(nameof(closeRequested));
        _state = _viewModel.State;
        _viewModel.StateChanged += OnViewModelStateChanged;
        _sortDropdown = GetRequiredObject<DropDown>("comments_sort_dropdown");
        _stack = GetRequiredObject<Stack>("comments_stack");
        GetRequiredObject<ScrolledWindow>("comments_scrolled_window");
        _emptyPage = GetRequiredObject<StatusPage>("comments_empty_page");
        _errorPage = GetRequiredObject<StatusPage>("comments_error_page");

        _itemIds = StringList.New([]);
        _selection = NoSelection.New(_itemIds);
        _factory = SignalListItemFactory.New();
        _factory.OnSetup += OnRowSetup;
        _factory.OnBind += OnRowBind;
        _factory.OnUnbind += OnRowUnbind;
        _factory.OnTeardown += OnRowTeardown;
        _list = GetRequiredObject<ListView>("comments_list");
        _list.Model = _selection;
        _list.Factory = _factory;
        _stack.VisibleChildName = "unavailable";
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

    private void OnSortDropdownNotify(object? sender, EventArgs args)
    {
        _viewModel.SetSortSelection(_sortDropdown.GetSelected());
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
        var visibleIds = state.VisibleComments.Select(row => row.Comment.Id).ToArray();
        _itemIds.Splice(0, _itemIds.GetNItems(), visibleIds);

        // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
        switch (state.Status)
        {
            case CommentsViewStatus.Unavailable:
                _stack.VisibleChildName = "unavailable";
                break;
            case CommentsViewStatus.Loading:
                _stack.VisibleChildName = "loading";
                break;
            case CommentsViewStatus.Error:
                _errorPage.Description = state.StatusMessage;
                _stack.VisibleChildName = "error";
                break;
            case CommentsViewStatus.Empty:
                _emptyPage.Description = state.StatusMessage;
                _stack.VisibleChildName = "empty";
                break;
            case CommentsViewStatus.List:
                _stack.VisibleChildName = "list";
                break;
        }
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
        _viewModel.Dispose();
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
        base.Dispose();
    }
}