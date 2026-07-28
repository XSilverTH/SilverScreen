namespace SilverScreen.Core.Models;

public sealed record YouTubeComment(
    string Id,
    string AuthorName,
    string Text,
    string PublishedTimeText,
    long LikeCount,
    string? ParentId = null);