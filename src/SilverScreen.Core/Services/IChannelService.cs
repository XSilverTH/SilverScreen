using SilverScreen.Core.Models;

namespace SilverScreen.Core.Services;

public interface IChannelService
{
    Task<ChannelPage> GetChannelAsync(string channelUrl, string fallbackName, ChannelVideoSort sort,
        CancellationToken cancellationToken);
}
