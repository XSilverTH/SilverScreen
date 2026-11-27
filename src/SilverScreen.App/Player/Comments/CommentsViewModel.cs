using System.Collections.ObjectModel;
using System.ComponentModel;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Core.Preferences;

namespace SilverScreen.Player.Comments;

public enum CommentsViewStatus
{
    Unavailable,
    Loading,
    Error,
    Empty,
    List
}

public sealed record CommentRowState(
    YouTubeComment Comment,
    int ReplyCount,
    bool RepliesExpanded,
    bool CanTranslate = false,
    bool IsTranslated = false,
    bool IsTranslating = false,
    string? TranslatedText = null,
    string? TranslationError = null,
    int Depth = 0,
    bool IsLastSibling = true,
    IReadOnlyList<bool>? AncestorHasMoreSiblings = null,
    bool HasMoreReplies = false,
    bool IsLoadingReplies = false,
    string? LoadMoreRepliesParentId = null,
    bool IsFirstSibling = true,
    bool IsLastInThread = true,
    string? ThreadRootCommentId = null,
    bool CanEdit = false,
    bool CanDelete = false,
    bool IsEditing = false,
    bool IsSavingEdit = false,
    string? EditError = null,
    bool IsDeleting = false,
    string? DeleteError = null,
    bool IsTextExpanded = false);

public sealed record CommentsViewState(
    CommentsViewStatus Status,
    IReadOnlyList<CommentRowState> VisibleComments,
    string StatusMessage,
    bool IsLoadingMore = false,
    bool HasMore = false,
    string PaginationLoadingMessage = "Loading more comments…",
    bool IsPostingComment = false,
    string? PostCommentError = null);

public sealed class CommentsViewModel(
    IYouTubeCommentService comments,
    IPreferencesService? preferences = null) : INotifyPropertyChanged, IDisposable
{
    public const int InitialPageSize = 20;
    public const int PageSizeIncrement = 20;
    private const string DefaultErrorMessage = "Comments could not be loaded. Try again shortly.";
    private static readonly ILogger Logger = Log.ForContext<CommentsViewModel>();
    private readonly IYouTubeCommentService _comments = comments ?? throw new ArgumentNullException(nameof(comments));
    private readonly IPreferencesService? _preferences = preferences;
    private readonly Dictionary<string, YouTubeComment> _commentsById = [];
    private readonly HashSet<string> _expandedCommentIds = [];
    private readonly HashSet<string> _expandedTextCommentIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loadingRepliesIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _translatedTexts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _showingTranslationIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _translatingIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _likingIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _translationErrors = new(StringComparer.Ordinal);
    private readonly HashSet<string> _editingIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _savingEditIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _editErrors = new(StringComparer.Ordinal);
    private readonly HashSet<string> _deletingIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _deleteErrors = new(StringComparer.Ordinal);
    private readonly Lock _loadGate = new();
    private readonly Dictionary<string, List<YouTubeComment>> _repliesByParentId = [];
    private readonly List<string> _topLevelCommentIds = [];
    private bool _disposed;
    private bool _hasLoadedCurrentVideo;
    private bool _hasMore;
    private bool _isPostingComment;
    private string? _postCommentError;
    private CancellationTokenSource? _loadCancellation;
    private long _loadGeneration;
    private CancellationTokenSource? _loadMoreCancellation;
    private long _loadMoreGeneration;
    private YouTubeCommentSort _sort = YouTubeCommentSort.Top;
    private string? _videoId;

    public CommentsViewState State { get; private set; } = new(CommentsViewStatus.Unavailable, [], string.Empty);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CancelLoad();
        PropertyChanged = null;
        StateChanged = null;
        _commentsById.Clear();
        _repliesByParentId.Clear();
        _topLevelCommentIds.Clear();
        _expandedCommentIds.Clear();
        _expandedTextCommentIds.Clear();
        _expandedTextCommentIds.Clear();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<CommentsViewState>? StateChanged;

    public void SetVideo(string? videoId)
    {
        var validVideoId = videoId is not null && PlaybackRequest.LooksLikeYouTubeVideoId(videoId)
            ? videoId
            : null;
        if (string.Equals(_videoId, validVideoId, StringComparison.Ordinal))
            return;

        CancelLoad();
        _videoId = validVideoId;
        _hasLoadedCurrentVideo = false;
        _hasMore = false;
        ClearComments();
        if (_videoId is null)
        {
            Publish(CommentsViewStatus.Unavailable, string.Empty);
            return;
        }

        StartLoad();
    }

    public void EnsureLoaded()
    {
        if (_disposed || _videoId is null || _hasLoadedCurrentVideo || _loadCancellation is not null)
            return;

        StartLoad();
    }

    public void ClearPostError()
    {
        lock (_loadGate)
        {
            if (_disposed || _postCommentError is null)
                return;

            _postCommentError = null;
            PublishVisibleState();
        }
    }

    public async Task<bool> PostCommentAsync(string text, string? authorName = null, string? authorAvatarUrl = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string videoId;
        long generation;
        var trimmedText = text.Trim();

        lock (_loadGate)
        {
            if (_disposed || _videoId is null || _isPostingComment)
                return false;

            _isPostingComment = true;
            _postCommentError = null;
            videoId = _videoId;
            generation = _loadGeneration;
            PublishVisibleState();
        }

        YouTubeCommentPostResult result;
        try
        {
            result = await _comments.PostCommentAsync(videoId, trimmedText).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (_loadGate)
            {
                _isPostingComment = false;
                if (!_disposed && generation == _loadGeneration && string.Equals(_videoId, videoId, StringComparison.Ordinal))
                    PublishVisibleState();
            }

            return false;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to post comment for video {VideoId}", videoId);
            result = new YouTubeCommentPostResult(false, ErrorMessage: "Could not post comment. Try again shortly.");
        }

        lock (_loadGate)
        {
            _isPostingComment = false;
            if (_disposed || generation != _loadGeneration || !string.Equals(_videoId, videoId, StringComparison.Ordinal))
                return false;

            if (result.IsSuccess && result.Comment is { } posted)
            {
                var resolvedAuthor = string.IsNullOrWhiteSpace(posted.AuthorName) ||
                                     string.Equals(posted.AuthorName, "YouTube user", StringComparison.Ordinal) ||
                                     string.Equals(posted.AuthorName, "You", StringComparison.Ordinal)
                    ? (string.IsNullOrWhiteSpace(authorName) ? posted.AuthorName : authorName)
                    : posted.AuthorName;
                var resolvedAvatar = string.IsNullOrWhiteSpace(posted.AuthorAvatarUrl)
                    ? authorAvatarUrl
                    : posted.AuthorAvatarUrl;

                var comment = posted with
                {
                    AuthorName = string.IsNullOrWhiteSpace(resolvedAuthor) ? "You" : resolvedAuthor,
                    AuthorAvatarUrl = resolvedAvatar
                };

                var alreadyExists = _commentsById.ContainsKey(comment.Id);
                _commentsById[comment.Id] = comment;
                if (!alreadyExists)
                    _topLevelCommentIds.Insert(0, comment.Id);

                _postCommentError = null;
                Publish(CommentsViewStatus.List, State.StatusMessage);
                return true;
            }

            _postCommentError = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "Could not post comment. Try again shortly."
                : result.ErrorMessage;
            PublishVisibleState();
            return false;
        }
    }

    public void BeginEdit(string commentId)
    {
        lock (_loadGate)
        {
            if (_disposed ||
                string.IsNullOrWhiteSpace(commentId) ||
                !_commentsById.TryGetValue(commentId, out var comment) ||
                string.IsNullOrWhiteSpace(comment.UpdateAction) ||
                _deletingIds.Contains(commentId))
                return;

            _editingIds.Add(commentId);
            _editErrors.Remove(commentId);
            PublishVisibleState();
        }
    }

    public void CancelEdit(string commentId)
    {
        lock (_loadGate)
        {
            if (_disposed || string.IsNullOrWhiteSpace(commentId) || _savingEditIds.Contains(commentId))
                return;

            if (_editingIds.Remove(commentId) | _editErrors.Remove(commentId))
                PublishVisibleState();
        }
    }

    public async Task<bool> SaveEditAsync(string commentId, string newText)
    {
        if (string.IsNullOrWhiteSpace(commentId) || string.IsNullOrWhiteSpace(newText))
            return false;

        YouTubeComment original;
        string updateAction;
        long generation;
        string? videoId;
        var trimmedText = newText.Trim();

        lock (_loadGate)
        {
            if (_disposed ||
                _savingEditIds.Contains(commentId) ||
                _deletingIds.Contains(commentId) ||
                !_commentsById.TryGetValue(commentId, out original!) ||
                string.IsNullOrWhiteSpace(original.UpdateAction))
                return false;

            updateAction = original.UpdateAction;
            _savingEditIds.Add(commentId);
            _editErrors.Remove(commentId);
            generation = _loadGeneration;
            videoId = _videoId;
            PublishVisibleState();
        }

        YouTubeCommentPostResult result;
        try
        {
            result = await _comments.UpdateCommentAsync(commentId, trimmedText, updateAction).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (_loadGate)
            {
                _savingEditIds.Remove(commentId);
                if (!_disposed && generation == _loadGeneration && string.Equals(_videoId, videoId, StringComparison.Ordinal))
                    PublishVisibleState();
            }

            return false;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to update comment {CommentId}", commentId);
            result = new YouTubeCommentPostResult(false, ErrorMessage: "Could not update comment. Try again shortly.");
        }

        lock (_loadGate)
        {
            _savingEditIds.Remove(commentId);
            if (_disposed || generation != _loadGeneration || !string.Equals(_videoId, videoId, StringComparison.Ordinal))
                return false;

            if (result.IsSuccess && result.Comment is { } updated)
            {
                if (_commentsById.TryGetValue(commentId, out var current))
                {
                    _commentsById[commentId] = updated with
                    {
                        Id = current.Id,
                        AuthorName = string.IsNullOrWhiteSpace(updated.AuthorName) ||
                                     string.Equals(updated.AuthorName, "YouTube user", StringComparison.Ordinal) ||
                                     string.Equals(updated.AuthorName, "You", StringComparison.Ordinal)
                            ? current.AuthorName
                            : updated.AuthorName,
                        AuthorAvatarUrl = string.IsNullOrWhiteSpace(updated.AuthorAvatarUrl)
                            ? current.AuthorAvatarUrl
                            : updated.AuthorAvatarUrl,
                        ParentId = current.ParentId,
                        ReplyCount = current.ReplyCount,
                        HasMoreReplies = current.HasMoreReplies,
                        UpdateAction = updated.UpdateAction ?? current.UpdateAction,
                        DeleteAction = updated.DeleteAction ?? current.DeleteAction,
                        IsEdited = true
                    };
                }

                _editingIds.Remove(commentId);
                _editErrors.Remove(commentId);
                _translatedTexts.Remove(commentId);
                _showingTranslationIds.Remove(commentId);
                _translationErrors.Remove(commentId);
                PublishVisibleState();
                return true;
            }

            _editErrors[commentId] = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "Could not update comment. Try again shortly."
                : result.ErrorMessage;
            PublishVisibleState();
            return false;
        }
    }

    public async Task<bool> DeleteCommentAsync(string commentId)
    {
        if (string.IsNullOrWhiteSpace(commentId))
            return false;

        string deleteAction;
        long generation;
        string? videoId;

        lock (_loadGate)
        {
            if (_disposed ||
                _deletingIds.Contains(commentId) ||
                !_commentsById.TryGetValue(commentId, out var comment) ||
                string.IsNullOrWhiteSpace(comment.DeleteAction))
                return false;

            deleteAction = comment.DeleteAction;
            _deletingIds.Add(commentId);
            _deleteErrors.Remove(commentId);
            PublishVisibleState();
            generation = _loadGeneration;
            videoId = _videoId;
        }

        YouTubeCommentActionResult result;
        try
        {
            result = await _comments.DeleteCommentAsync(commentId, deleteAction).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (_loadGate)
            {
                _deletingIds.Remove(commentId);
                if (!_disposed && generation == _loadGeneration && string.Equals(_videoId, videoId, StringComparison.Ordinal))
                    PublishVisibleState();
            }

            return false;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to delete comment {CommentId}", commentId);
            result = new YouTubeCommentActionResult(false, "Could not delete comment. Try again shortly.");
        }

        lock (_loadGate)
        {
            _deletingIds.Remove(commentId);
            if (_disposed || generation != _loadGeneration || !string.Equals(_videoId, videoId, StringComparison.Ordinal))
                return false;

            if (result.IsSuccess)
            {
                RemoveCommentTree(commentId);
                Publish(_topLevelCommentIds.Count == 0 ? CommentsViewStatus.Empty : CommentsViewStatus.List,
                    _topLevelCommentIds.Count == 0 ? "No comments are available for this video." : State.StatusMessage);
                return true;
            }

            _deleteErrors[commentId] = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "Could not delete comment. Try again shortly."
                : result.ErrorMessage;
            PublishVisibleState();
            return false;
        }
    }

    private void RemoveCommentTree(string commentId)
    {
        if (_commentsById.TryGetValue(commentId, out var removed) &&
            !string.IsNullOrWhiteSpace(removed.ParentId) &&
            _repliesByParentId.TryGetValue(removed.ParentId, out var siblingReplies))
        {
            siblingReplies.RemoveAll(r => string.Equals(r.Id, commentId, StringComparison.Ordinal));
            if (_commentsById.TryGetValue(removed.ParentId, out var parent))
            {
                _commentsById[removed.ParentId] = parent with
                {
                    ReplyCount = Math.Max(0, parent.ReplyCount - 1)
                };
            }
        }

        var toRemove = new Queue<string>();
        toRemove.Enqueue(commentId);
        while (toRemove.Count > 0)
        {
            var id = toRemove.Dequeue();
            _commentsById.Remove(id);
            _topLevelCommentIds.RemoveAll(t => string.Equals(t, id, StringComparison.Ordinal));
            _expandedCommentIds.Remove(id);
            _expandedTextCommentIds.Remove(id);
            _loadingRepliesIds.Remove(id);
            _translatedTexts.Remove(id);
            _showingTranslationIds.Remove(id);
            _translatingIds.Remove(id);
            _likingIds.Remove(id);
            _translationErrors.Remove(id);
            _editingIds.Remove(id);
            _savingEditIds.Remove(id);
            _editErrors.Remove(id);
            _deletingIds.Remove(id);
            _deleteErrors.Remove(id);

            if (_repliesByParentId.Remove(id, out var children))
            {
                foreach (var child in children)
                    toRemove.Enqueue(child.Id);
            }
        }
    }

    public void Retry()
    {
        if (_disposed || _videoId is null)
            return;

        StartLoad();
    }

    public void SetSortSelection(uint selected)
    {
        SetSort(selected == 1 ? YouTubeCommentSort.Newest : YouTubeCommentSort.Top);
    }

    private void SetSort(YouTubeCommentSort sort)
    {
        if (sort is not YouTubeCommentSort.Top and not YouTubeCommentSort.Newest)
            throw new ArgumentOutOfRangeException(nameof(sort), sort, null);

        _sort = sort;
        _hasMore = false;
        if (_videoId is not null)
            StartLoad();
    }

    public void ToggleCommentTextExpanded(string commentId)
    {
        lock (_loadGate)
        {
            if (_disposed || !_commentsById.ContainsKey(commentId))
                return;

            if (!_expandedTextCommentIds.Add(commentId))
                _expandedTextCommentIds.Remove(commentId);

            PublishVisibleState();
        }
    }

    public void ToggleReplies(string commentId)
    {
        bool shouldLoadReplies;
        lock (_loadGate)
        {
            if (_disposed || !_commentsById.TryGetValue(commentId, out var comment))
                return;

            var hasLoadedReplies = _repliesByParentId.ContainsKey(commentId);
            if (!hasLoadedReplies && !comment.HasMoreReplies)
                return;

            if (!_expandedCommentIds.Add(commentId))
            {
                _expandedCommentIds.Remove(commentId);
                PublishVisibleState();
                return;
            }

            shouldLoadReplies = !hasLoadedReplies && comment.HasMoreReplies && !_loadingRepliesIds.Contains(commentId);
            if (!shouldLoadReplies)
            {
                PublishVisibleState();
                return;
            }
        }

        LoadMoreRepliesAsync(commentId).FireAndForget(Logger);
    }

    public async Task LoadMoreRepliesAsync(string parentCommentId)
    {
        long generation;
        string? videoId;
        lock (_loadGate)
        {
            if (_disposed || _videoId is null || !_commentsById.TryGetValue(parentCommentId, out var parent))
                return;

            if (!parent.HasMoreReplies || _loadingRepliesIds.Contains(parentCommentId))
                return;

            _expandedCommentIds.Add(parentCommentId);
            _loadingRepliesIds.Add(parentCommentId);
            generation = _loadGeneration;
            videoId = _videoId;
            PublishVisibleState();
        }

        YouTubeCommentsResult result;
        try
        {
            result = await _comments.LoadRepliesAsync(parentCommentId).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (_loadGate)
            {
                _loadingRepliesIds.Remove(parentCommentId);
                if (!_disposed && generation == _loadGeneration && string.Equals(_videoId, videoId, StringComparison.Ordinal))
                    PublishVisibleState();
            }

            return;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to load replies for comment {CommentId}", parentCommentId);
            result = new YouTubeCommentsResult([], false, "Could not load replies.");
        }

        lock (_loadGate)
        {
            _loadingRepliesIds.Remove(parentCommentId);
            if (_disposed || generation != _loadGeneration || !string.Equals(_videoId, videoId, StringComparison.Ordinal))
                return;

            if (result.IsSuccess)
                MergeComments(result.Comments);

            PublishVisibleState();
        }
    }

    public async Task ToggleTranslationAsync(string commentId)
    {
        if (_disposed)
            return;

        YouTubeComment? comment;
        long generation;
        string? videoId;
        lock (_loadGate)
        {
            if (!_commentsById.TryGetValue(commentId, out comment))
                return;

            if (_translatingIds.Contains(commentId))
                return;

            if (_translatedTexts.ContainsKey(commentId))
            {
                if (!_showingTranslationIds.Add(commentId))
                    _showingTranslationIds.Remove(commentId);
                _translationErrors.Remove(commentId);
                PublishVisibleState();
                return;
            }

            _translatingIds.Add(commentId);
            _translationErrors.Remove(commentId);
            generation = _loadGeneration;
            videoId = _videoId;
        }

        PublishVisibleState();

        var targetLanguage = _preferences?.GetPreferences().GetEffectiveYouTubeLanguage()
                             ?? AppPreferences.DefaultYouTubeLanguage;
        YouTubeCommentTranslationResult result;
        try
        {
            result = await _comments
                .TranslateCommentAsync(commentId, comment.Text, targetLanguage)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (_loadGate)
            {
                _translatingIds.Remove(commentId);
            }
            return;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to translate comment {CommentId}", commentId);
            result = new YouTubeCommentTranslationResult(false, ErrorMessage: "Could not translate comment.");
        }

        lock (_loadGate)
        {
            _translatingIds.Remove(commentId);
            if (_disposed || generation != _loadGeneration || !string.Equals(_videoId, videoId, StringComparison.Ordinal))
                return;

            if (result.IsSuccess && !string.IsNullOrEmpty(result.TranslatedText))
            {
                _translatedTexts[commentId] = result.TranslatedText;
                _showingTranslationIds.Add(commentId);
                _translationErrors.Remove(commentId);
            }
            else
            {
                _translationErrors[commentId] = result.ErrorMessage ?? "Could not translate comment.";
            }

            PublishVisibleState();
        }
    }

    public async Task ToggleLikeAsync(string commentId)
    {
        if (_disposed || string.IsNullOrWhiteSpace(commentId))
            return;

        YouTubeComment original;
        bool targetLiked;
        string? actionToken;
        long generation;
        string? videoId;

        lock (_loadGate)
        {
            if (_disposed || _likingIds.Contains(commentId))
                return;

            if (!_commentsById.TryGetValue(commentId, out original!))
                return;

            targetLiked = !original.IsLiked;
            actionToken = targetLiked ? original.LikeAction : original.UnlikeAction;
            if (string.IsNullOrWhiteSpace(actionToken))
                return;

            _likingIds.Add(commentId);
            generation = _loadGeneration;
            videoId = _videoId;

            _commentsById[commentId] = original with
            {
                IsLiked = targetLiked,
                LikeCount = Math.Max(0, original.LikeCount + (targetLiked ? 1 : -1))
            };
        }

        PublishVisibleState();

        var success = false;
        try
        {
            success = await _comments
                .SetCommentLikeAsync(commentId, targetLiked, actionToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (_loadGate)
            {
                _likingIds.Remove(commentId);
            }
            return;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to toggle like for comment {CommentId}", commentId);
        }

        lock (_loadGate)
        {
            _likingIds.Remove(commentId);
            if (_disposed || generation != _loadGeneration || !string.Equals(_videoId, videoId, StringComparison.Ordinal))
                return;

            if (!success)
            {
                if (_commentsById.ContainsKey(commentId))
                    _commentsById[commentId] = original;
                PublishVisibleState();
            }
        }
    }

    public async Task LoadMoreAsync()
    {
        if (_disposed || _videoId is null || !_hasLoadedCurrentVideo || !_hasMore)
            return;

        CancellationTokenSource cancellation;
        long generation;
        string videoId;
        YouTubeCommentSort sort;

        lock (_loadGate)
        {
            if (_loadCancellation is not null || _loadMoreCancellation is not null)
                return;

            cancellation = new CancellationTokenSource();
            _loadMoreCancellation = cancellation;
            generation = ++_loadMoreGeneration;
            videoId = _videoId;
            sort = _sort;
        }

        Publish(State.Status, State.StatusMessage, true);

        var cancellationToken = cancellation.Token;
        YouTubeCommentsResult result;
        try
        {
            result = await _comments.LoadNextPageAsync(PageSizeIncrement, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to load more comments for video");
            result = new YouTubeCommentsResult([], false, "Could not load more comments.");
        }

        lock (_loadGate)
        {
            if (_disposed || cancellationToken.IsCancellationRequested || generation != _loadMoreGeneration ||
                !string.Equals(_videoId, videoId, StringComparison.Ordinal) || _sort != sort)
                return;

            if (ReferenceEquals(_loadMoreCancellation, cancellation))
            {
                _loadMoreCancellation = null;
                cancellation.Dispose();
            }

            if (result.IsSuccess)
            {
                _hasMore = result.HasMore;
                ApplyComments(result.Comments);
                Publish(CommentsViewStatus.List, result.StatusMessage);
            }
            else
            {
                Publish(CommentsViewStatus.List, State.StatusMessage);
            }
        }
    }

    private void StartLoad()
    {
        if (_disposed || _videoId is null)
            return;

        CancelLoad();
        _hasLoadedCurrentVideo = false;
        _hasMore = false;
        ClearComments();
        Publish(CommentsViewStatus.Loading, string.Empty);
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        var generation = ++_loadGeneration;
        LoadCommentsAsync(_videoId, _sort, generation, cancellation).FireAndForget(Logger);
    }

    private async Task LoadCommentsAsync(string videoId, YouTubeCommentSort sort, long generation,
        CancellationTokenSource cancellation)
    {
        var cancellationToken = cancellation.Token;
        YouTubeCommentsResult result;
        try
        {
            result = await _comments.LoadFirstPageAsync(videoId, sort, InitialPageSize, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to load comments for video");
            result = new YouTubeCommentsResult([], false, DefaultErrorMessage);
        }

        lock (_loadGate)
        {
            if (_disposed || cancellationToken.IsCancellationRequested || generation != _loadGeneration ||
                !string.Equals(_videoId, videoId, StringComparison.Ordinal))
                return;

            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                _loadCancellation = null;
                cancellation.Dispose();
            }

            _hasLoadedCurrentVideo = true;
            if (!result.IsSuccess)
            {
                _hasMore = false;
                ClearComments();
                Publish(CommentsViewStatus.Error,
                    string.IsNullOrWhiteSpace(result.StatusMessage) ? DefaultErrorMessage : result.StatusMessage);
                return;
            }

            _hasMore = result.HasMore;
            ApplyComments(result.Comments);
            Publish(result.Comments.Count == 0 ? CommentsViewStatus.Empty : CommentsViewStatus.List,
                result.StatusMessage);
        }
    }

    private void ApplyComments(IReadOnlyList<YouTubeComment> comments)
    {
        _commentsById.Clear();
        _repliesByParentId.Clear();
        _topLevelCommentIds.Clear();

        foreach (var comment in comments)
            _commentsById[comment.Id] = comment;

        foreach (var comment in comments)
        {
            if (string.IsNullOrWhiteSpace(comment.ParentId) || !_commentsById.ContainsKey(comment.ParentId))
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
    }

    private void MergeComments(IReadOnlyList<YouTubeComment> comments)
    {
        if (comments.Count == 0)
            return;

        foreach (var comment in comments)
            _commentsById[comment.Id] = comment;

        _repliesByParentId.Clear();
        _topLevelCommentIds.Clear();

        foreach (var comment in _commentsById.Values)
        {
            if (string.IsNullOrWhiteSpace(comment.ParentId) || !_commentsById.ContainsKey(comment.ParentId))
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
    }

    private void ClearComments()
    {
        _commentsById.Clear();
        _repliesByParentId.Clear();
        _topLevelCommentIds.Clear();
        _expandedCommentIds.Clear();
        _expandedTextCommentIds.Clear();
        _loadingRepliesIds.Clear();
        _translatedTexts.Clear();
        _showingTranslationIds.Clear();
        _translatingIds.Clear();
        _likingIds.Clear();
        _translationErrors.Clear();
        _editingIds.Clear();
        _savingEditIds.Clear();
        _editErrors.Clear();
        _deletingIds.Clear();
        _deleteErrors.Clear();
        _isPostingComment = false;
        _postCommentError = null;
    }

    private void PublishVisibleState()
    {
        var status = State.Status;
        if (status is not CommentsViewStatus.List and not CommentsViewStatus.Empty)
            return;

        Publish(status, State.StatusMessage, State.IsLoadingMore);
    }

    private void Publish(CommentsViewStatus status, string statusMessage, bool isLoadingMore = false)
    {
        State = new CommentsViewState(
            status,
            BuildVisibleComments(),
            statusMessage,
            isLoadingMore,
            _hasMore,
            IsPostingComment: _isPostingComment,
            PostCommentError: _postCommentError);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
        StateChanged?.Invoke(this, State);
    }

    private readonly record struct VisibleCommentNode(
        string CommentId,
        int Depth,
        bool IsFirstSibling,
        bool IsLastSibling,
        IReadOnlyList<bool> AncestorHasMoreSiblings,
        string? LoadMoreRepliesParentId);

    private ReadOnlyCollection<CommentRowState> BuildVisibleComments()
    {
        if (_commentsById.Count == 0)
            return [];

        var visibleNodes = new List<VisibleCommentNode>(_commentsById.Count);
        var ancestors = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < _topLevelCommentIds.Count; i++)
        {
            var isFirstTopLevel = i == 0;
            var isLastTopLevel = i == _topLevelCommentIds.Count - 1;
            AddVisibleComment(_topLevelCommentIds[i], 0, isFirstTopLevel, isLastTopLevel, [], visibleNodes, ancestors, null);
        }

        var targetLanguage = _preferences?.GetPreferences().GetEffectiveYouTubeLanguage()
                             ?? AppPreferences.DefaultYouTubeLanguage;
        var visibleComments = new CommentRowState[visibleNodes.Count];
        string? currentRootId = null;
        for (var index = 0; index < visibleNodes.Count; index++)
        {
            var node = visibleNodes[index];
            var commentId = node.CommentId;
            if (node.Depth == 0)
                currentRootId = commentId;

            var isLastInThread = index == visibleNodes.Count - 1 || visibleNodes[index + 1].Depth == 0;
            var comment = _commentsById[commentId];
            var hasTranslated = _translatedTexts.TryGetValue(commentId, out var translatedText);
            var isTranslated = hasTranslated && _showingTranslationIds.Contains(commentId);
            var isTranslating = _translatingIds.Contains(commentId);
            var canTranslate = hasTranslated ||
                               isTranslating ||
                               CommentTranslationDetector.NeedsTranslation(comment.Text, targetLanguage);
            _translationErrors.TryGetValue(commentId, out var translationError);

            var loadedReplyCount = _repliesByParentId.TryGetValue(commentId, out var replies) ? replies.Count : 0;
            var effectiveReplyCount = Math.Max(comment.ReplyCount, loadedReplyCount);
            _editErrors.TryGetValue(commentId, out var editError);
            _deleteErrors.TryGetValue(commentId, out var deleteError);

            visibleComments[index] = new CommentRowState(
                comment,
                effectiveReplyCount,
                _expandedCommentIds.Contains(commentId),
                canTranslate,
                isTranslated,
                isTranslating,
                translatedText,
                translationError,
                node.Depth,
                node.IsLastSibling,
                node.AncestorHasMoreSiblings,
                comment.HasMoreReplies,
                _loadingRepliesIds.Contains(commentId),
                node.LoadMoreRepliesParentId,
                node.IsFirstSibling,
                isLastInThread,
                currentRootId,
                !string.IsNullOrWhiteSpace(comment.UpdateAction),
                !string.IsNullOrWhiteSpace(comment.DeleteAction),
                _editingIds.Contains(commentId),
                _savingEditIds.Contains(commentId),
                editError,
                _deletingIds.Contains(commentId),
                deleteError,
                _expandedTextCommentIds.Contains(commentId));
        }

        return Array.AsReadOnly(visibleComments);
    }

    private void AddVisibleComment(
        string commentId,
        int depth,
        bool isFirstSibling,
        bool isLastSibling,
        IReadOnlyList<bool> ancestorHasMoreSiblings,
        List<VisibleCommentNode> visibleNodes,
        HashSet<string> ancestors,
        string? loadMoreRepliesParentId)
    {
        if (!ancestors.Add(commentId))
            return;

        visibleNodes.Add(new VisibleCommentNode(
            commentId,
            depth,
            isFirstSibling,
            isLastSibling,
            ancestorHasMoreSiblings,
            loadMoreRepliesParentId));

        if (_expandedCommentIds.Contains(commentId) &&
            _commentsById.TryGetValue(commentId, out var comment) &&
            _repliesByParentId.TryGetValue(commentId, out var replies) &&
            replies.Count > 0)
        {
            IReadOnlyList<bool> nextAncestors = depth == 0
                ? []
                : [.. ancestorHasMoreSiblings, !isLastSibling];
            for (var i = 0; i < replies.Count; i++)
            {
                var isFirstReply = i == 0;
                var isLastReply = i == replies.Count - 1;
                var nextLoadMoreParentId = isLastReply && comment.HasMoreReplies ? commentId : null;
                AddVisibleComment(
                    replies[i].Id,
                    depth + 1,
                    isFirstReply,
                    isLastReply,
                    nextAncestors,
                    visibleNodes,
                    ancestors,
                    nextLoadMoreParentId);
            }
        }

        ancestors.Remove(commentId);
    }

    private void CancelLoad()
    {
        lock (_loadGate)
        {
            _loadGeneration++;
            _loadMoreGeneration++;
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _loadCancellation = null;
            _loadMoreCancellation?.Cancel();
            _loadMoreCancellation?.Dispose();
            _loadMoreCancellation = null;
        }
    }
}