namespace SilverScreen.Core.Player.Comments;

public sealed record YouTubeCommentActionResult(
    bool IsSuccess,
    string? ErrorMessage = null);
