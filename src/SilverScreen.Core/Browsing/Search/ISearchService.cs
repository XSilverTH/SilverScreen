namespace SilverScreen.Core.Browsing.Search;

public interface ISearchService
{
    Task<SearchResultPage> SearchAsync(SearchRequest request, CancellationToken cancellationToken);
}