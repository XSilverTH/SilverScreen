using SilverScreen.Core.Browsing.Common;
namespace SilverScreen.Core.Browsing.History;

public sealed record AuthenticatedHistoryResult(
    AuthenticatedHistoryStatus Status,
    FeedPage FeedPage,
    string StatusMessage);