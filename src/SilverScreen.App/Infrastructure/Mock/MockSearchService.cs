using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Features.Search;

namespace SilverScreen.Infrastructure.Mock;

public sealed class MockSearchService : ISearchService
{
    public Task<SearchResultPage> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(SearchResultPage.Empty);
    }

    public bool IsLikelyYouTubeUrl(string text)
    {
        return YouTubeUrlParser.Parse(text).Kind is not YouTubeUrlKind.NotYouTube and not YouTubeUrlKind.Invalid;
    }
}
