using Gtk;

using SilverScreen.Browsing.Components;
using SilverScreen.Browsing.Home;
using SilverScreen.Browsing.Channel;
using SilverScreen.Browsing.Search;
using SilverScreen.Browsing.History;
using SilverScreen.Player.Comments;
using SilverScreen.Queue;
using SilverScreen.Account.Profile;
using SilverScreen.Account.Auth;
using SilverScreen.Account.Session;
using SilverScreen.Preferences;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;

namespace SilverScreen.Player.Comments;

public partial class CommentsView : ViewBase<Box>
{

    private readonly Action _closeRequested;
    private readonly SignalListItemFactory _factory;
    private readonly StringList _itemIds;
    private readonly Dictionary<Widget, CommentRowView> _rowsByCell = [];
    private readonly NoSelection _selection;
    private readonly CommentsViewModel _viewModel;
    private bool _disposed;
    private CommentsViewState _state;

    public CommentsView(CommentsViewModel viewModel, Action closeRequested)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _closeRequested = closeRequested ?? throw new ArgumentNullException(nameof(closeRequested));
        _state = _viewModel.State;
        _viewModel.StateChanged += OnViewModelStateChanged;

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
        var visibleIds = state.VisibleComments.Select(row => row.Comment.Id).ToArray();
        _itemIds.Splice(0, _itemIds.GetNItems(), visibleIds);

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
        comments_list.Dispose();
        _selection.Dispose();
        _factory.Dispose();
        _itemIds.Dispose();
        base.Dispose();
    }
}