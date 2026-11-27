namespace SilverScreen.Core.Player.Comments;

public sealed record YouTubeComment(
    string Id,
    string AuthorName,
    string Text,
    string PublishedTimeText,
    long LikeCount,
    string? ParentId = null,
    int ReplyCount = 0,
    bool HasMoreReplies = false,
    string? AuthorAvatarUrl = null,
    bool IsLiked = false,
    string? LikeAction = null,
    string? UnlikeAction = null,
    string? UpdateAction = null,
    string? DeleteAction = null,
    bool IsEdited = false);