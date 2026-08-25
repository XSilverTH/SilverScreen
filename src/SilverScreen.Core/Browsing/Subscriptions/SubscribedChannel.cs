namespace SilverScreen.Core.Browsing.Subscriptions;

public sealed record SubscribedChannel(
    string Id,
    string Title,
    string Url,
    string? AvatarUrl,
    string? Description = null,
    long? SubscriberCount = null);
