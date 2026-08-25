using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Core.Browsing.Subscriptions;

public sealed record AuthenticatedSubscriptionsFeedResult(
    AuthenticatedSubscriptionsStatus Status,
    FeedPage FeedPage,
    string StatusMessage)
{
    public override string ToString()
    {
        return
            $"Status: {Status}, VideoCount: {FeedPage.Videos.Count}, HasContinuation: {!string.IsNullOrEmpty(FeedPage.ContinuationToken)}, StatusMessage: {StatusMessage}";
    }
}
