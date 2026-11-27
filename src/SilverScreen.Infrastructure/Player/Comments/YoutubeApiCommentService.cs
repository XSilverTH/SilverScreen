using Serilog;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Infrastructure.YouTube;
using YoutubeAPI.Exceptions;
using YoutubeAPI.Models.Comments;
using YoutubeAPI.Models.Continuations;
using YoutubeAPI.Models.Enums;
using YoutubeAPI.Models.ValueTypes;

namespace SilverScreen.Infrastructure.Player.Comments;

/// <summary>Loads comment-thread pages and retains YoutubeAPI continuation state between requests.</summary>
public sealed class YoutubeApiCommentService(IYouTubeClientProvider clientProvider)
    : IYouTubeCommentService, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<YoutubeApiCommentService>();

    private readonly IYouTubeClientProvider _clientProvider =
        clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));

    private readonly Lock _gate = new();
    private readonly HashSet<string> _loadedCommentIds = [with(StringComparer.Ordinal)];
    private readonly List<YouTubeComment> _loadedComments = [];
    private readonly Dictionary<string, CommentRepliesContinuation> _replyContinuationsByParentId =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private bool _disposed;
    private YouTubeCommentSort _sort;
    private CommentThreadsContinuation? _threadContinuation;
    private string? _videoId;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _requestGate.Dispose();
    }

    public async Task<YouTubeCommentsResult> LoadFirstPageAsync(
        string videoId,
        YouTubeCommentSort sort,
        int count = 20,
        CancellationToken cancellationToken = default)
    {
        if (!VideoId.TryParse(videoId, out var parsedVideoId) || count < 1)
            return Failure("Comments are unavailable for this video.");

        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Reset(parsedVideoId.Value, sort);
            return await FetchThreadPageAsync(parsedVideoId, sort, null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public async Task<YouTubeCommentsResult> LoadNextPageAsync(
        int count = 20,
        CancellationToken cancellationToken = default)
    {
        if (count < 1)
            return Failure("Comments are unavailable for this video.");

        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? videoId;
            YouTubeCommentSort sort;
            CommentThreadsContinuation? threadContinuation;
            lock (_gate)
            {
                videoId = _videoId;
                sort = _sort;
                threadContinuation = _threadContinuation;
            }

            if (videoId is null)
                return Failure("Comments are unavailable for this video.");
            if (threadContinuation is null)
                return Snapshot("No additional comments are available for this video.", false);

            var parsedVideoId = VideoId.Parse(videoId);
            return await FetchThreadPageAsync(parsedVideoId, sort, threadContinuation, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public async Task<YouTubeCommentsResult> LoadRepliesAsync(
        string parentCommentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(parentCommentId))
            return Failure("Could not load replies.");

        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? videoId;
            CommentRepliesContinuation? replyContinuation;
            lock (_gate)
            {
                videoId = _videoId;
                _replyContinuationsByParentId.TryGetValue(parentCommentId, out replyContinuation);
            }

            if (videoId is null)
                return Failure("Comments are unavailable for this video.");
            if (replyContinuation is null)
                return Snapshot("No additional replies are available.", HasMore());

            return await FetchReplyPageAsync(parentCommentId, replyContinuation, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public async Task<YouTubeCommentTranslationResult> TranslateCommentAsync(
        string commentId,
        string text,
        string? targetLanguage = null,
        CancellationToken cancellationToken = default)
    {
        if (!CommentId.TryParse(commentId, out var parsedCommentId) || string.IsNullOrWhiteSpace(text))
            return new YouTubeCommentTranslationResult(false, ErrorMessage: "Comment could not be translated.");

        try
        {
            var client = _clientProvider.GetClient();
            var translated = await client.Comments
                .TranslateAsync(parsedCommentId, text, targetLanguage, cancellationToken)
                .ConfigureAwait(false);
            return new YouTubeCommentTranslationResult(true, translated);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (YouTubeException exception)
        {
            Logger.Warning(exception, "YoutubeAPI failed to translate comment {CommentId}", commentId);
            return new YouTubeCommentTranslationResult(false, ErrorMessage: "Could not translate comment.");
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Unexpected failure translating comment {CommentId}", commentId);
            return new YouTubeCommentTranslationResult(false, ErrorMessage: "Could not translate comment.");
        }
    }

    private async Task<YouTubeCommentsResult> FetchThreadPageAsync(
        VideoId videoId,
        YouTubeCommentSort sort,
        CommentThreadsContinuation? continuation,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _clientProvider.GetClient();
            var apiSort = sort switch
            {
                YouTubeCommentSort.Top => CommentSort.Top,
                YouTubeCommentSort.Newest => CommentSort.Newest,
                _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, null)
            };
            var page = continuation is null
                ? await client.Comments.GetThreadsPageAsync(videoId, apiSort, cancellationToken)
                    .ConfigureAwait(false)
                : await client.Comments.GetThreadsPageAsync(continuation, cancellationToken)
                    .ConfigureAwait(false);

            lock (_gate)
            {
                foreach (var thread in page.Items)
                {
                    var parentId = thread.TopLevel.Id.Value;
                    var replyCount = Math.Max(thread.ReplyCount ?? thread.Replies.Count, thread.Replies.Count);
                    var hasMoreReplies = thread.NextReplies is not null;
                    AddComment(thread.TopLevel, null, replyCount, hasMoreReplies);
                    foreach (var reply in thread.Replies)
                        AddComment(reply, parentId);
                    if (thread.NextReplies is not null)
                        _replyContinuationsByParentId[parentId] = thread.NextReplies;
                }

                _threadContinuation = page.Next;
            }

            return Snapshot(
                _loadedComments.Count == 0 ? "No comments were returned for this video." : "Comments loaded.",
                HasMore());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CommentsUnavailableException exception)
        {
            Logger.Warning(exception, "YoutubeAPI reports comments unavailable for video");
            return Failure("Comments are unavailable for this video.");
        }
        catch (YouTubeException exception)
        {
            Logger.Warning(exception, "YoutubeAPI failed to fetch comment threads for video");
            return Failure("Could not load comments.");
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Unexpected failure fetching comment threads for video");
            return Failure("Could not load comments.");
        }
    }

    private async Task<YouTubeCommentsResult> FetchReplyPageAsync(
        string parentId,
        CommentRepliesContinuation continuation,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await _clientProvider.GetClient().Comments
                .GetRepliesPageAsync(continuation, cancellationToken)
                .ConfigureAwait(false);
            lock (_gate)
            {
                foreach (var reply in page.Items)
                    AddComment(reply, parentId);

                if (page.Next is not null)
                    _replyContinuationsByParentId[parentId] = page.Next;
                else
                    _replyContinuationsByParentId.Remove(parentId);

                UpdateParentReplyMetadata(parentId, page.Next is not null);
            }

            return Snapshot("Replies loaded.", HasMore());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (YouTubeException exception)
        {
            Logger.Warning(exception, "YoutubeAPI failed to fetch comment replies");
            return Failure("Could not load replies.");
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Unexpected failure fetching comment replies");
            return Failure("Could not load replies.");
        }
    }

    public async Task<bool> SetCommentLikeAsync(
        string commentId,
        bool like,
        string? actionToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(commentId) || string.IsNullOrWhiteSpace(actionToken))
            return false;

        try
        {
            var client = _clientProvider.GetClient();
            await client.Comments
                .PerformActionAsync(actionToken, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to {Action} comment {CommentId}", like ? "like" : "unlike", commentId);
            return false;
        }
    }

    public async Task<YouTubeCommentPostResult> PostCommentAsync(
        string videoId,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (!VideoId.TryParse(videoId, out var parsedVideoId) || string.IsNullOrWhiteSpace(text))
            return new YouTubeCommentPostResult(false, ErrorMessage: "Could not post comment.");

        var trimmedText = text.Trim();
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var client = _clientProvider.GetClient();
            var created = await client.Comments
                .CreateCommentAsync(parsedVideoId, trimmedText, cancellationToken)
                .ConfigureAwait(false);

            var mapped = MapComment(created, null);
            lock (_gate)
            {
                if (string.Equals(_videoId, parsedVideoId.Value, StringComparison.Ordinal) &&
                    _loadedCommentIds.Add(mapped.Id))
                {
                    _loadedComments.Insert(0, mapped);
                }
            }

            return new YouTubeCommentPostResult(true, mapped);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var message = MapMutationError(exception, "post", videoId);
            return new YouTubeCommentPostResult(false, ErrorMessage: message);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public async Task<YouTubeCommentPostResult> UpdateCommentAsync(
        string commentId,
        string text,
        string? updateAction,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(commentId) ||
            string.IsNullOrWhiteSpace(text) ||
            string.IsNullOrWhiteSpace(updateAction))
            return new YouTubeCommentPostResult(false, ErrorMessage: "Could not update comment.");

        var trimmedText = text.Trim();
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var client = _clientProvider.GetClient();
            var updated = await client.Comments
                .UpdateCommentAsync(updateAction, trimmedText, cancellationToken)
                .ConfigureAwait(false);

            var mapped = MapComment(updated, null) with { IsEdited = true };
            lock (_gate)
            {
                for (var i = 0; i < _loadedComments.Count; i++)
                {
                    if (!string.Equals(_loadedComments[i].Id, commentId, StringComparison.Ordinal))
                        continue;

                    var existing = _loadedComments[i];
                    _loadedComments[i] = mapped with
                    {
                        Id = existing.Id,
                        ParentId = existing.ParentId,
                        ReplyCount = existing.ReplyCount,
                        HasMoreReplies = existing.HasMoreReplies,
                        UpdateAction = mapped.UpdateAction ?? existing.UpdateAction,
                        DeleteAction = mapped.DeleteAction ?? existing.DeleteAction
                    };
                    break;
                }
            }

            return new YouTubeCommentPostResult(true, mapped);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var message = MapMutationError(exception, "update", commentId);
            return new YouTubeCommentPostResult(false, ErrorMessage: message);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public async Task<YouTubeCommentActionResult> DeleteCommentAsync(
        string commentId,
        string? deleteAction,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(commentId) || string.IsNullOrWhiteSpace(deleteAction))
            return new YouTubeCommentActionResult(false, "Could not delete comment.");

        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var client = _clientProvider.GetClient();
            await client.Comments
                .DeleteCommentAsync(deleteAction, cancellationToken)
                .ConfigureAwait(false);

            lock (_gate)
            {
                for (var i = _loadedComments.Count - 1; i >= 0; i--)
                {
                    var item = _loadedComments[i];
                    if (string.Equals(item.Id, commentId, StringComparison.Ordinal) ||
                        string.Equals(item.ParentId, commentId, StringComparison.Ordinal))
                    {
                        _loadedCommentIds.Remove(item.Id);
                        _loadedComments.RemoveAt(i);
                    }
                }
            }

            return new YouTubeCommentActionResult(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var message = MapMutationError(exception, "delete", commentId);
            return new YouTubeCommentActionResult(false, message);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private static string MapMutationError(Exception exception, string verb, string targetId)
    {
        switch (exception)
        {
            case AuthenticationRequiredException:
                return $"Sign in to YouTube to {verb} a comment.";
            case AuthenticationExpiredException:
                return $"Your YouTube session expired. Sign in again to {verb} comments.";
            case CommentsUnavailableException:
                return "Comments are disabled for this video.";
            case RateLimitedException:
                return "You are making requests too quickly. Try again shortly.";
            case YouTubeException:
                Logger.Warning(exception, "YoutubeAPI failed to {Verb} comment {TargetId}", verb, targetId);
                return $"Could not {verb} comment. Try again shortly.";
            default:
                Logger.Warning(exception, "Unexpected failure trying to {Verb} comment {TargetId}", verb, targetId);
                return $"Could not {verb} comment. Try again shortly.";
        }
    }

    private void Reset(string videoId, YouTubeCommentSort sort)
    {
        lock (_gate)
        {
            _videoId = videoId;
            _sort = sort;
            _threadContinuation = null;
            _replyContinuationsByParentId.Clear();
            _loadedComments.Clear();
            _loadedCommentIds.Clear();
        }
    }

    private void AddComment(Comment comment, string? parentId, int replyCount = 0, bool hasMoreReplies = false)
    {
        if (!_loadedCommentIds.Add(comment.Id.Value)) return;
        _loadedComments.Add(MapComment(comment, parentId, replyCount, hasMoreReplies));
    }

    private static YouTubeComment MapComment(
        Comment comment,
        string? parentId,
        int replyCount = 0,
        bool hasMoreReplies = false)
    {
        var authorName = string.IsNullOrWhiteSpace(comment.Author.Name) ? "YouTube user" : comment.Author.Name;
        return new YouTubeComment(
            comment.Id.Value,
            authorName,
            comment.Text,
            comment.PublishedText,
            Math.Max(comment.LikeCount.GetValueOrDefault(), 0),
            parentId,
            replyCount,
            hasMoreReplies,
            comment.Author.Avatar?.Url.ToString(),
            comment.IsLiked,
            comment.LikeAction,
            comment.UnlikeAction,
            comment.UpdateAction,
            comment.DeleteAction,
            comment.IsEdited);
    }

    private void UpdateParentReplyMetadata(string parentId, bool hasMoreReplies)
    {
        var loadedRepliesCount = 0;
        var parentIndex = -1;
        for (var i = 0; i < _loadedComments.Count; i++)
        {
            var item = _loadedComments[i];
            if (string.Equals(item.Id, parentId, StringComparison.Ordinal))
                parentIndex = i;
            else if (string.Equals(item.ParentId, parentId, StringComparison.Ordinal))
                loadedRepliesCount++;
        }

        if (parentIndex >= 0)
        {
            var parent = _loadedComments[parentIndex];
            _loadedComments[parentIndex] = parent with
            {
                ReplyCount = Math.Max(parent.ReplyCount, loadedRepliesCount),
                HasMoreReplies = hasMoreReplies
            };
        }
    }

    private bool HasMore()
    {
        lock (_gate)
        {
            return _threadContinuation is not null;
        }
    }

    private YouTubeCommentsResult Snapshot(string message, bool hasMore)
    {
        lock (_gate)
        {
            return new YouTubeCommentsResult([.. _loadedComments], true, message, hasMore);
        }
    }

    private static YouTubeCommentsResult Failure(string statusMessage)
    {
        return new YouTubeCommentsResult([], false, statusMessage);
    }
}