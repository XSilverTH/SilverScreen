using SilverScreen.Core.Models;

namespace SilverScreen.Core.Services;

public interface IFeedService
{
    FeedPage GetHomeFeed();
}