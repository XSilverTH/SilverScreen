using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Serilog;
using SilverScreen.Core.Browsing.Search;
using SilverScreen.Core.Common;

namespace SilverScreen.Infrastructure.Browsing.Search;

public sealed class YouTubeSearchSuggestionService : ISearchSuggestionService, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<YouTubeSearchSuggestionService>();
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly Uri SuggestEndpoint = new("https://suggestqueries.google.com/complete/search");
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _cache = new(StringComparer.OrdinalIgnoreCase);

    private readonly bool _disposeHttpClient;
    private readonly HttpClient _httpClient;

    public YouTubeSearchSuggestionService()
        : this(CreateDefaultHttpClient(), true)
    {
    }

    public YouTubeSearchSuggestionService(HttpClient httpClient, bool disposeHttpClient = false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _disposeHttpClient = disposeHttpClient;
    }

    public void Dispose()
    {
        if (_disposeHttpClient) _httpClient.Dispose();
    }

    public async Task<IReadOnlyList<string>> GetSuggestionsAsync(string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var trimmed = query.Trim();

        // If the query is an URL or looks like a URL/video ID, do not query suggestions
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            YouTubeUrlParser.Parse(trimmed).Kind != YouTubeUrlKind.NotYouTube)
            return [];

        if (_cache.TryGetValue(trimmed, out var cached))
        {
            Logger.Debug("YouTube search suggestion cache hit for {Query}", trimmed);
            return cached;
        }

        var requestUri = new UriBuilder(SuggestEndpoint)
        {
            Query = $"client=firefox&ds=yt&q={Uri.EscapeDataString(trimmed)}"
        }.Uri;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
            {
                Logger.Warning("YouTube search suggestions returned HTTP status {StatusCode} for query {Query}",
                    response.StatusCode, trimmed);
                return [];
            }

            var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var suggestions = ParseSuggestions(responseText);

            _cache.TryAdd(trimmed, suggestions);
            Logger.Debug("Fetched {Count} suggestions for query {Query}", suggestions.Count, trimmed);
            return suggestions;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or NotSupportedException)
        {
            Logger.Warning(exception, "Failed to fetch search suggestions for query {Query}", trimmed);
            return [];
        }
    }

    private static List<string> ParseSuggestions(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return [];

        var text = responseText.Trim();

        // Handle JSONP callback if present (e.g., window.google.ac.h([...]))
        if (text.StartsWith("window.google.ac.h(", StringComparison.OrdinalIgnoreCase))
        {
            var startIndex = text.IndexOf('(') + 1;
            var endIndex = text.LastIndexOf(')');
            if (startIndex > 0 && endIndex > startIndex) text = text[startIndex..endIndex].Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 2)
                return [];

            var suggestionsElement = root[1];
            if (suggestionsElement.ValueKind != JsonValueKind.Array)
                return [];

            var results = new List<string>();
            foreach (var suggestion in suggestionsElement.EnumerateArray().Select(item => item.ValueKind switch
                     {
                         JsonValueKind.String => item.GetString(),
                         JsonValueKind.Array when item.GetArrayLength() > 0 &&
                                                  item[0].ValueKind == JsonValueKind.String =>
                             item[0].GetString(),
                         _ => null
                     }).Where(suggestion => !string.IsNullOrWhiteSpace(suggestion) &&
                                            !results.Contains(suggestion, StringComparer.OrdinalIgnoreCase)))
                results.Add(suggestion!.Trim());

            return results;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        return new HttpClient { Timeout = DefaultTimeout };
    }
}