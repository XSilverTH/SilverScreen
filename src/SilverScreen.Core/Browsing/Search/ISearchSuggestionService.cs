namespace SilverScreen.Core.Browsing.Search;

public interface ISearchSuggestionService
{
    Task<IReadOnlyList<string>> GetSuggestionsAsync(string query, CancellationToken cancellationToken = default);
}