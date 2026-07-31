namespace SilverScreen.Core.Models;

public sealed record AuthenticatedHistoryResult(
    AuthenticatedHistoryStatus Status,
    FeedPage FeedPage,
    string StatusMessage);
