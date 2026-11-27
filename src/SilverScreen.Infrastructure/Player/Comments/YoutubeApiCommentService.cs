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
        var authorName = string.IsNullOrWhiteSpace(comment.Author.Name) ? "YouTube user" : comment.Author.Name;
        _loadedComments.Add(new YouTubeComment(
            comment.Id.Value,
            authorName,
            comment.Text,
            comment.PublishedText,
            Math.Max(comment.LikeCount.GetValueOrDefault(), 0),
            parentId,
            replyCount,
            hasMoreReplies));
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