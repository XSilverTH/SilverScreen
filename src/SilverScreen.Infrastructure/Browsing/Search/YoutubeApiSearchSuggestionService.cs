using Serilog;
using SilverScreen.Core.Browsing.Search;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Infrastructure.Browsing.Search;

/// <summary>Loads YouTube query completions through YoutubeAPI.</summary>
public sealed class YoutubeApiSearchSuggestionService(
    IYouTubeClientProvider clientProvider) : ISearchSuggestionService
{
    private static readonly ILogger Logger = Log.ForContext<YoutubeApiSearchSuggestionService>();
    private readonly IYouTubeClientProvider _clientProvider =
        clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));

    public async Task<IReadOnlyList<string>> GetSuggestionsAsync(
        string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        try
        {
            return await _clientProvider.GetClient().Suggestions
                .GetAsync(query.Trim(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.Debug(exception, "YoutubeAPI suggestions failed for {Query}", query);
            return [];
        }
    }
}
