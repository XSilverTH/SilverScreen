namespace SilverScreen.Core.Services;

public interface ISearchSuggestionService
{
    Task<IReadOnlyList<string>> GetSuggestionsAsync(string query, CancellationToken cancellationToken = default);
}