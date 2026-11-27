namespace SilverScreen.Core.Player.Comments;

public sealed record YouTubeCommentPostResult(
    bool IsSuccess,
    YouTubeComment? Comment = null,
    string? ErrorMessage = null);
