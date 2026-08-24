using SilverScreen.Infrastructure.Common;
using System.Collections.ObjectModel;
using Serilog;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Search;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Queue;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure;

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
    string StatusMessage);

public sealed class CommentsViewModel(IYouTubeCommentService comments) : IDisposable
{
    private const string DefaultErrorMessage = "Comments could not be loaded. Try again shortly.";
    private static readonly ILogger Logger = Log.ForContext<CommentsViewModel>();
    private readonly IYouTubeCommentService _comments = comments ?? throw new ArgumentNullException(nameof(comments));
    private readonly Dictionary<string, YouTubeComment> _commentsById = [];
    private readonly HashSet<string> _expandedCommentIds = [];
    private readonly Lock _loadGate = new();
    private readonly Dictionary<string, List<YouTubeComment>> _repliesByParentId = [];
    private readonly List<string> _topLevelCommentIds = [];
    private bool _disposed;
    private bool _hasLoadedCurrentVideo;
    private CancellationTokenSource? _loadCancellation;
    private long _loadGeneration;
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
        ClearComments();
        Publish(CommentsViewStatus.Unavailable, string.Empty);
    }

    public void EnsureLoaded()
    {
        if (_disposed || _videoId is null || _hasLoadedCurrentVideo || _loadCancellation is not null)
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

    private void StartLoad()
    {
        if (_disposed || _videoId is null)
            return;

        CancelLoad();
        _hasLoadedCurrentVideo = false;
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
            result = await _comments.GetCommentsAsync(videoId, sort, cancellationToken).ConfigureAwait(false);
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
                ClearComments();
                Publish(CommentsViewStatus.Error,
                    string.IsNullOrWhiteSpace(result.StatusMessage) ? DefaultErrorMessage : result.StatusMessage);
                return;
            }

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
        _expandedCommentIds.Clear();

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

        Publish(status, State.StatusMessage);
    }

    private void Publish(CommentsViewStatus status, string statusMessage)
    {
        State = new CommentsViewState(status, BuildVisibleComments(), statusMessage);
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
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _loadCancellation = null;
        }
    }
}