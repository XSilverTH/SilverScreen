namespace SilverScreen.Core.Player.Comments;

/// <summary>Loads comments as a stateful sequence of YoutubeAPI comment-thread pages.</summary>
public interface IYouTubeCommentService
{
    /// <summary>Starts a new comment-thread sequence for a video and sort order.</summary>
    Task<YouTubeCommentsResult> LoadFirstPageAsync(
        string videoId,
        YouTubeCommentSort sort,
        int count = 20,
        CancellationToken cancellationToken = default);

    /// <summary>Loads the next retained YoutubeAPI thread or reply continuation.</summary>
    Task<YouTubeCommentsResult> LoadNextPageAsync(
        int count = 20,
        CancellationToken cancellationToken = default);
}