namespace SilverScreen.Core.Browsing.Channel;

public interface IChannelService
{
    Task<ChannelPage> GetChannelAsync(string channelUrl, string fallbackName, ChannelVideoSort sort,
        string? continuationToken, int count, CancellationToken cancellationToken);
}