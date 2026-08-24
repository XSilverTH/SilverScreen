namespace SilverScreen.Core.Player.Comments;

public sealed record YouTubeCommentsResult(
    IReadOnlyList<YouTubeComment> Comments,
    bool IsSuccess,
    string StatusMessage);