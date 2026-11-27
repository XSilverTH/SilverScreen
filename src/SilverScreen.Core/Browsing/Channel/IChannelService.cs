namespace SilverScreen.Core.Browsing.Channel;

public sealed record ChannelSubscriptionResult(
    bool IsSuccess,
    bool IsSubscribed,
    string? Message = null,
    string? ChannelId = null);

public interface IChannelService
{
    Task<ChannelPage> GetChannelAsync(string channelUrl, string fallbackName, ChannelVideoSort sort,
        string? continuationToken, int count, CancellationToken cancellationToken);

    Task<ChannelSubscriptionResult> SubscribeAsync(string channelIdOrUrl,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ChannelSubscriptionResult(false, false, "Subscribing is not supported."));
    }

    Task<ChannelSubscriptionResult> UnsubscribeAsync(string channelIdOrUrl,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ChannelSubscriptionResult(false, true, "Unsubscribing is not supported."));
    }
}