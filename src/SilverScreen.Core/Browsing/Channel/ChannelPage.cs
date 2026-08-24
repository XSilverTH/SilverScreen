using SilverScreen.Core.Browsing.Common;
namespace SilverScreen.Core.Browsing.Channel;

public enum ChannelVideoSort
{
    Newest,
    Oldest,
    Popular
}

public sealed record ChannelPage(
    string Url,
    string Name,
    string? Description,
    string? AvatarUrl,
    long? SubscriberCount,
    IReadOnlyList<VideoSummary> Videos,
    ChannelVideoSort Sort,
    string? StatusMessage = null,
    bool IsSuccess = true,
    int? NextStartIndex = null)
{
    public static ChannelPage Failed(string url, string name, ChannelVideoSort sort, string message)
    {
        return new ChannelPage(url, name, null, null, null, [], sort, message, false);
    }
}