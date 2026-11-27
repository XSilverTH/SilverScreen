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

    /// <summary>Loads the next retained YoutubeAPI thread continuation.</summary>
    Task<YouTubeCommentsResult> LoadNextPageAsync(
        int count = 20,
        CancellationToken cancellationToken = default);

    /// <summary>Loads the next page of replies for a specific parent comment.</summary>
    Task<YouTubeCommentsResult> LoadRepliesAsync(
        string parentCommentId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new YouTubeCommentsResult([], false, "Comment replies are not supported."));
    }

    /// <summary>Translates a comment into the specified target language (or the configured host language).</summary>
    Task<YouTubeCommentTranslationResult> TranslateCommentAsync(
        string commentId,
        string text,
        string? targetLanguage = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new YouTubeCommentTranslationResult(
            false,
            ErrorMessage: "Comment translation is not supported."));
    }
}