namespace SilverScreen.Core.Models;

public sealed record YouTubeCommentsResult(
    IReadOnlyList<YouTubeComment> Comments,
    bool IsSuccess,
    string StatusMessage);