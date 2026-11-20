using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Core.Browsing.Subscriptions;

/// <summary>Loads the signed-in account's subscribed channels and subscription feed from YouTube.</summary>
public interface IAuthenticatedSubscriptionsService
{
    // ReSharper disable UnusedParameter.Global
    Task<AuthenticatedSubscriptionsFeedResult> LoadFirstFeedPageAsync(
        int count = VideoFeedConstants.DefaultPageSize,
        CancellationToken cancellationToken = default);

    Task<AuthenticatedSubscriptionsFeedResult> LoadNextFeedPageAsync(
        int count = VideoFeedConstants.DefaultPageSize,
        CancellationToken cancellationToken = default);
    // ReSharper restore UnusedParameter.Global

    Task<SubscribedChannelsResult> LoadSubscribedChannelsAsync(
        CancellationToken cancellationToken = default);
}