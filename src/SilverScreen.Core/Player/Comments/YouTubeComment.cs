namespace SilverScreen.Core.Player.Comments;

public sealed record YouTubeComment(
    string Id,
    string AuthorName,
    string Text,
    string PublishedTimeText,
    long LikeCount,
    string? ParentId = null,
    int ReplyCount = 0,
    bool HasMoreReplies = false);