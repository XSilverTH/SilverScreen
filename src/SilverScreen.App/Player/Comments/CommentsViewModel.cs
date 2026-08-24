using System.Collections.ObjectModel;
using Serilog;
using SilverScreen.Core.Player;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Infrastructure.Common;
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
    bool RepliesExpanded);

public sealed record CommentsViewState(
    CommentsViewStatus Status,
    IReadOnlyList<CommentRowState> VisibleComments,
    string StatusMessage,
    bool IsLoadingMore = false,
    bool HasMore = false,
    string PaginationLoadingMessage = "Loading more comments…");

public sealed class CommentsViewModel(IYouTubeCommentService comments) : IDisposable
{
    public const int InitialPageSize = 20;
    public const int PageSizeIncrement = 20;
    private const string DefaultErrorMessage = "Comments could not be loaded. Try again shortly.";
    private static readonly ILogger Logger = Log.ForContext<CommentsViewModel>();
    private readonly IYouTubeCommentService _comments = comments ?? throw new ArgumentNullException(nameof(comments));
    private readonly Dictionary<string, YouTubeComment> _commentsById = [];
    private readonly HashSet<string> _expandedCommentIds = [];
    private readonly Lock _loadGate = new();
    private readonly Dictionary<string, List<YouTubeComment>> _repliesByParentId = [];
    private readonly List<string> _topLevelCommentIds = [];
    private int _currentMaxComments = InitialPageSize;
    private bool _disposed;
    private bool _hasLoadedCurrentVideo;
    private bool _hasMore;
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
        StateChanged = null;
        _commentsById.Clear();
        _repliesByParentId.Clear();
        _topLevelCommentIds.Clear();
        _expandedCommentIds.Clear();
    }

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
        _currentMaxComments = InitialPageSize;
        _hasMore = false;
        ClearComments();
        Publish(CommentsViewStatus.Unavailable, string.Empty);
    }

    public void EnsureLoaded()
    {
        if (_disposed || _videoId is null || _hasLoadedCurrentVideo || _loadCancellation is not null)
            return;

        StartLoad();
    }

    public void Refresh()
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
        _currentMaxComments = InitialPageSize;
        _hasMore = false;
        if (_videoId is not null)
            StartLoad();
    }

    public void ToggleReplies(string commentId)
    {
        if (_disposed || !_repliesByParentId.ContainsKey(commentId))
            return;

        if (!_expandedCommentIds.Add(commentId))
            _expandedCommentIds.Remove(commentId);

        PublishVisibleState();
    }

    public async Task LoadMoreAsync()
    {
        if (_disposed || _videoId is null || !_hasLoadedCurrentVideo || !_hasMore)
            return;

        CancellationTokenSource cancellation;
        long generation;
        int nextMaxComments;
        string videoId;
        YouTubeCommentSort sort;

        lock (_loadGate)
        {
            if (_loadCancellation is not null || _loadMoreCancellation is not null)
                return;

            cancellation = new CancellationTokenSource();
            _loadMoreCancellation = cancellation;
            generation = ++_loadMoreGeneration;
            nextMaxComments = _currentMaxComments + PageSizeIncrement;
            videoId = _videoId;
            sort = _sort;
        }

        Publish(State.Status, State.StatusMessage, isLoadingMore: true);

        var cancellationToken = cancellation.Token;
        YouTubeCommentsResult result;
        try
        {
            result = await _comments.GetCommentsAsync(videoId, sort, nextMaxComments, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to load more comments for video {VideoId}", videoId);
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
                _currentMaxComments = nextMaxComments;
                _hasMore = result.HasMore;
                ApplyComments(result.Comments);
                Publish(CommentsViewStatus.List, result.StatusMessage, isLoadingMore: false);
            }
            else
            {
                Publish(CommentsViewStatus.List, State.StatusMessage, isLoadingMore: false);
            }
        }
    }

    private void StartLoad()
    {
        if (_disposed || _videoId is null)
            return;

        CancelLoad();
        _hasLoadedCurrentVideo = false;
        _currentMaxComments = InitialPageSize;
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
            result = await _comments.GetCommentsAsync(videoId, sort, _currentMaxComments, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to load comments for video {VideoId}", videoId);
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

    private void ClearComments()
    {
        _commentsById.Clear();
        _repliesByParentId.Clear();
        _topLevelCommentIds.Clear();
        _expandedCommentIds.Clear();
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
        State = new CommentsViewState(status, BuildVisibleComments(), statusMessage, isLoadingMore, _hasMore);
        StateChanged?.Invoke(this, State);
    }

    private ReadOnlyCollection<CommentRowState> BuildVisibleComments()
    {
        if (_commentsById.Count == 0)
            return [];

        var visibleIds = new List<string>(_commentsById.Count);
        var ancestors = new HashSet<string>(StringComparer.Ordinal);
        foreach (var commentId in _topLevelCommentIds)
            AddVisibleComment(commentId, visibleIds, ancestors);

        var visibleComments = new CommentRowState[visibleIds.Count];
        for (var index = 0; index < visibleIds.Count; index++)
        {
            var commentId = visibleIds[index];
            var comment = _commentsById[commentId];
            visibleComments[index] = new CommentRowState(
                comment,
                _repliesByParentId.TryGetValue(commentId, out var replies) ? replies.Count : 0,
                _expandedCommentIds.Contains(commentId));
        }

        return Array.AsReadOnly(visibleComments);
    }

    private void AddVisibleComment(string commentId, List<string> visibleIds, HashSet<string> ancestors)
    {
        if (!ancestors.Add(commentId))
            return;

        visibleIds.Add(commentId);
        if (_expandedCommentIds.Contains(commentId) && _repliesByParentId.TryGetValue(commentId, out var replies))
            foreach (var reply in replies)
                AddVisibleComment(reply.Id, visibleIds, ancestors);

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