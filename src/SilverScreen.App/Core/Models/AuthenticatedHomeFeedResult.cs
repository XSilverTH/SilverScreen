namespace SilverScreen.Core.Models;

public sealed record AuthenticatedHomeFeedResult(
    AuthenticatedHomeFeedStatus Status,
    FeedPage FeedPage,
    string StatusMessage)
{
    public override string ToString()
    {
        return $"Status: {Status}, VideoCount: {FeedPage.Videos.Count}, HasContinuation: {!string.IsNullOrEmpty(FeedPage.ContinuationToken)}, StatusMessage: {StatusMessage}";
    }
}
