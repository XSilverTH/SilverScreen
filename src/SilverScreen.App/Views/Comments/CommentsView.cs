using Adw;
using Gtk;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using XSTH.Blueprint.Helpers;
using Functions = GLib.Functions;

namespace SilverScreen.Views.Comments;

public partial class CommentsView : ViewBase<Box>
{
    private readonly Action _closeRequested;
    private readonly IYouTubeCommentService _comments;
    private readonly Dictionary<string, YouTubeComment> _commentsById = [];
    private readonly HashSet<string> _expandedCommentIds = [];
    private readonly StatusPage _emptyPage;
    private readonly StatusPage _errorPage;
    private readonly SignalListItemFactory _factory;
    private readonly StringList _itemIds;
    private readonly ListView _list;
    private readonly Dictionary<Widget, CommentRowView> _rowsByCell = [];
    private readonly Dictionary<string, List<YouTubeComment>> _repliesByParentId = [];
    private readonly ScrolledWindow _scrolledWindow;
    private readonly NoSelection _selection;
    private readonly DropDown _sortDropdown;
    private readonly Stack _stack;
    private readonly List<string> _topLevelCommentIds = [];
    private bool _disposed;
    private bool _hasLoadedCurrentVideo;
    private CancellationTokenSource? _loadCancellation;
    private long _loadGeneration;
    private string? _videoId;

    public CommentsView(IYouTubeCommentService comments, Action closeRequested)
    {
        _comments = comments ?? throw new ArgumentNullException(nameof(comments));
        _closeRequested = closeRequested ?? throw new ArgumentNullException(nameof(closeRequested));
        _sortDropdown = GetRequiredObject<DropDown>("comments_sort_dropdown");
        _stack = GetRequiredObject<Stack>("comments_stack");
        _scrolledWindow = GetRequiredObject<ScrolledWindow>("comments_scrolled_window");
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
        var validVideoId = videoId is not null && PlaybackRequest.LooksLikeYouTubeVideoId(videoId) ? videoId : null;
        if (string.Equals(_videoId, validVideoId, StringComparison.Ordinal))
            return;

        CancelLoad();
        _videoId = validVideoId;
        _hasLoadedCurrentVideo = false;
        ClearComments();
        _stack.VisibleChildName = "unavailable";
    }

    public void EnsureLoaded()
    {
        if (_disposed || _videoId is null || _hasLoadedCurrentVideo || _loadCancellation is not null)
            return;

        StartLoad();
    }

    private void OnCloseButtonClicked(object? sender, EventArgs args)
    {
        _closeRequested();
    }

    private void OnRetryButtonClicked(object? sender, EventArgs args)
    {
        StartLoad();
    }

    private void OnSortDropdownNotify(object? sender, EventArgs args)
    {
        if (_videoId is not null)
            StartLoad();
    }

    private void StartLoad()
    {
        if (_disposed || _videoId is null)
            return;

        CancelLoad();
        _hasLoadedCurrentVideo = false;
        ClearComments();
        _stack.VisibleChildName = "loading";
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        var generation = ++_loadGeneration;
        _ = LoadCommentsAsync(_videoId, SortAt(_sortDropdown.GetSelected()), generation, cancellation.Token);
    }

    private async Task LoadCommentsAsync(string videoId, YouTubeCommentSort sort, long generation,
        CancellationToken cancellationToken)
    {
        YouTubeCommentsResult result;
        try
        {
            result = await _comments.GetCommentsAsync(videoId, sort, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            result = new YouTubeCommentsResult([], false, "Comments could not be loaded. Try again shortly.");
        }

        Functions.IdleAdd(0, () =>
        {
            if (_disposed || cancellationToken.IsCancellationRequested || generation != _loadGeneration ||
                !string.Equals(_videoId, videoId, StringComparison.Ordinal))
                return false;

            _loadCancellation?.Dispose();
            _loadCancellation = null;
            _hasLoadedCurrentVideo = true;
            Render(result);
            return false;
        });
    }

    private void Render(YouTubeCommentsResult result)
    {
        if (!result.IsSuccess)
        {
            ClearComments();
            _errorPage.Description = string.IsNullOrWhiteSpace(result.StatusMessage)
                ? "Comments could not be loaded. Try again shortly."
                : result.StatusMessage;
            _stack.VisibleChildName = "error";
            return;
        }

        ApplyComments(result.Comments);
        if (result.Comments.Count == 0)
        {
            _emptyPage.Description = result.StatusMessage;
            _stack.VisibleChildName = "empty";
            return;
        }

        _stack.VisibleChildName = "list";
    }

    private void ApplyComments(IReadOnlyList<YouTubeComment> comments)
    {
        _commentsById.Clear();
        _repliesByParentId.Clear();
        _topLevelCommentIds.Clear();
        _expandedCommentIds.Clear();

        foreach (var comment in comments)
            _commentsById[comment.Id] = comment;

        foreach (var comment in comments)
        {
            if (string.IsNullOrWhiteSpace(comment.ParentId) ||
                !_commentsById.ContainsKey(comment.ParentId))
            {
                _topLevelCommentIds.Add(comment.Id);
                continue;
            }

            if (!_repliesByParentId.TryGetValue(comment.ParentId, out var replies))
            {
                replies = [];
                _repliesByParentId.Add(comment.ParentId, replies);
            }

            replies.Add(comment);
        }

        RebuildVisibleComments();
    }

    private void ClearComments()
    {
        _commentsById.Clear();
        _repliesByParentId.Clear();
        _topLevelCommentIds.Clear();
        _expandedCommentIds.Clear();
        _itemIds.Splice(0, _itemIds.GetNItems(), []);
    }

    private void ToggleReplies(string commentId)
    {
        if (!_repliesByParentId.ContainsKey(commentId))
            return;

        if (!_expandedCommentIds.Add(commentId))
            _expandedCommentIds.Remove(commentId);

        RebuildVisibleComments();
    }

    private void RebuildVisibleComments()
    {
        var visibleIds = new List<string>(_commentsById.Count);
        var ancestors = new HashSet<string>(StringComparer.Ordinal);
        foreach (var commentId in _topLevelCommentIds)
            AddVisibleComment(commentId, visibleIds, ancestors);

        _itemIds.Splice(0, _itemIds.GetNItems(), visibleIds.ToArray());
    }

    private void AddVisibleComment(string commentId, List<string> visibleIds, HashSet<string> ancestors)
    {
        if (!ancestors.Add(commentId))
            return;

        visibleIds.Add(commentId);
        if (_expandedCommentIds.Contains(commentId) &&
            _repliesByParentId.TryGetValue(commentId, out var replies))
        {
            foreach (var reply in replies)
                AddVisibleComment(reply.Id, visibleIds, ancestors);
        }

        ancestors.Remove(commentId);
    }

    private void OnRowSetup(object? sender, SignalListItemFactory.SetupSignalArgs args)
    {
        if (args.Object is not ListItem listItem)
            return;

        var row = new CommentRowView(ToggleReplies);
        listItem.Child = row.Widget;
        _rowsByCell[row.Widget] = row;
    }

    private void OnRowBind(object? sender, SignalListItemFactory.BindSignalArgs args)
    {
        if (args.Object is not ListItem { Child: { } child, Item: StringObject { String: { } id } } ||
            !_rowsByCell.TryGetValue(child, out var row) ||
            !_commentsById.TryGetValue(id, out var comment))
            return;

        row.Bind(
            comment,
            _repliesByParentId.TryGetValue(comment.Id, out var replies) ? replies.Count : 0,
            _expandedCommentIds.Contains(comment.Id));
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

    private static YouTubeCommentSort SortAt(uint selected)
    {
        return selected == 1 ? YouTubeCommentSort.Newest : YouTubeCommentSort.Top;
    }

    private void CancelLoad()
    {
        _loadGeneration++;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }

    public new void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CancelLoad();
        _factory.OnSetup -= OnRowSetup;
        _factory.OnBind -= OnRowBind;
        _factory.OnUnbind -= OnRowUnbind;
        _factory.OnTeardown -= OnRowTeardown;
        foreach (var row in _rowsByCell.Values)
            row.Dispose();

        _rowsByCell.Clear();
        _commentsById.Clear();
        _list.Dispose();
        _selection.Dispose();
        _factory.Dispose();
        _itemIds.Dispose();
        base.Dispose();
    }
}