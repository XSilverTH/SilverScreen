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
public sealed class YoutubeApiCommentService(IYouTubeClientProvider clientProvider) : IYouTubeCommentService, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<YoutubeApiCommentService>();
    private readonly IYouTubeClientProvider _clientProvider =
        clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly List<YouTubeComment> _loadedComments = [];
    private readonly HashSet<string> _loadedCommentIds = new(StringComparer.Ordinal);
    private readonly Queue<CommentRepliesContinuation> _replyContinuations = [];
    private CommentThreadsContinuation? _threadContinuation;
    private YouTubeCommentSort _sort;
    private string? _videoId;
    private bool _disposed;

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
            CommentRepliesContinuation? replyContinuation;
            lock (_gate)
            {
                videoId = _videoId;
                sort = _sort;
                threadContinuation = _threadContinuation;
                replyContinuation = threadContinuation is null && _replyContinuations.Count > 0
                    ? _replyContinuations.Dequeue()
                    : null;
            }

            if (videoId is null)
                return Failure("Comments are unavailable for this video.");
            if (threadContinuation is null && replyContinuation is null)
                return Snapshot("No additional comments are available for this video.", false);

            var parsedVideoId = VideoId.Parse(videoId);
            return threadContinuation is not null
                ? await FetchThreadPageAsync(parsedVideoId, sort, threadContinuation, cancellationToken)
                    .ConfigureAwait(false)
                : await FetchReplyPageAsync(replyContinuation!, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _requestGate.Dispose();
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
                    AddComment(thread.TopLevel, null);
                    foreach (var reply in thread.Replies)
                        AddComment(reply, thread.TopLevel.Id.Value);
                    if (thread.NextReplies is not null)
                        _replyContinuations.Enqueue(thread.NextReplies);
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
            Logger.Warning(exception, "YoutubeAPI reports comments unavailable for video {VideoId}", videoId.Value);
            return Failure("Comments are unavailable for this video.");
        }
        catch (YouTubeException exception)
        {
            Logger.Warning(exception, "YoutubeAPI failed to fetch comment threads for video {VideoId}", videoId.Value);
            return Failure("Could not load comments.");
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Unexpected failure fetching comment threads for video {VideoId}", videoId.Value);
            return Failure("Could not load comments.");
        }
    }

    private async Task<YouTubeCommentsResult> FetchReplyPageAsync(
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
                    AddComment(reply, continuation.Target);
                if (page.Next is not null)
                    _replyContinuations.Enqueue(page.Next);
            }

            return Snapshot("Comments loaded.", HasMore());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (YouTubeException exception)
        {
            Logger.Warning(exception, "YoutubeAPI failed to fetch comment replies");
            return Failure("Could not load comments.");
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Unexpected failure fetching comment replies");
            return Failure("Could not load comments.");
        }
    }

    private void Reset(string videoId, YouTubeCommentSort sort)
    {
        lock (_gate)
        {
            _videoId = videoId;
            _sort = sort;
            _threadContinuation = null;
            _replyContinuations.Clear();
            _loadedComments.Clear();
            _loadedCommentIds.Clear();
        }
    }

    private void AddComment(Comment comment, string? parentId)
    {
        if (_loadedCommentIds.Add(comment.Id.Value))
        {
            var authorName = string.IsNullOrWhiteSpace(comment.Author.Name) ? "YouTube user" : comment.Author.Name;
            _loadedComments.Add(new YouTubeComment(
                comment.Id.Value,
                authorName,
                comment.Text,
                comment.PublishedText,
                Math.Max(comment.LikeCount.GetValueOrDefault(), 0),
                parentId));
        }
    }

    private bool HasMore()
    {
        lock (_gate)
            return _threadContinuation is not null || _replyContinuations.Count > 0;
    }

    private YouTubeCommentsResult Snapshot(string message, bool hasMore)
    {
        lock (_gate)
            return new YouTubeCommentsResult([.. _loadedComments], true, message, hasMore);
    }

    private static YouTubeCommentsResult Failure(string statusMessage)
    {
        return new YouTubeCommentsResult([], false, statusMessage);
    }
}
