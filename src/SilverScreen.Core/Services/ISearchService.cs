using SilverScreen.Core.Models;

namespace SilverScreen.Core.Services;

public interface ISearchService
{
    Task<SearchResultPage> SearchAsync(SearchRequest request, CancellationToken cancellationToken);
}