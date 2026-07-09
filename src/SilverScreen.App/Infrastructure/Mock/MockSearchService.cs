using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Features.Search;

namespace SilverScreen.Infrastructure.Mock;

public sealed class MockSearchService : ISearchService
{
    public FeedPage Search(SearchRequest request)
    {
        return FeedPage.Empty;
    }

    public bool IsLikelyYouTubeUrl(string text)
    {
        return YouTubeUrlParser.Parse(text).Kind is not YouTubeUrlKind.NotYouTube and not YouTubeUrlKind.Invalid;
    }
}
