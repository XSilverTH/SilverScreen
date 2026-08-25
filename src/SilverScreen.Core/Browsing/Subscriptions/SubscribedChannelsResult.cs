namespace SilverScreen.Core.Browsing.Subscriptions;

public sealed record SubscribedChannelsResult(
    AuthenticatedSubscriptionsStatus Status,
    IReadOnlyList<SubscribedChannel> Channels,
    string StatusMessage);
