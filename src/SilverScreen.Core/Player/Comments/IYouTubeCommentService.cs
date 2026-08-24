namespace SilverScreen.Core.Player.Comments;

public interface IYouTubeCommentService
{
    Task<YouTubeCommentsResult> GetCommentsAsync(
        string videoId,
        YouTubeCommentSort sort,
        int maxComments = 20,
        CancellationToken cancellationToken = default);
}