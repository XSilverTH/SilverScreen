namespace SilverScreen.Core.Player.Comments;

public sealed record YouTubeCommentTranslationResult(
    bool IsSuccess,
    string? TranslatedText = null,
    string? ErrorMessage = null);
