namespace SilverScreen.Core.Browsing.Channel;

public interface IChannelService
{
    Task<ChannelPage> GetChannelAsync(string channelUrl, string fallbackName, ChannelVideoSort sort, int startIndex,
        int count, CancellationToken cancellationToken);
}