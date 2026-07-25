using SilverScreen.Core.Models;

namespace SilverScreen.Core.Services;

public interface IYouTubeCommentService
{
    Task<YouTubeCommentsResult> GetCommentsAsync(
        string videoId,
        YouTubeCommentSort sort,
        CancellationToken cancellationToken = default);
}
