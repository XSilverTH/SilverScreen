using SilverScreen.Core.Models;

namespace SilverScreen.Core.Services;

public interface ISearchService
{
    FeedPage Search(SearchRequest request);

    bool IsLikelyYouTubeUrl(string text);
}
