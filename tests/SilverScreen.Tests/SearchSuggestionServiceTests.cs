using System.Net;
using SilverScreen.Infrastructure.Features.Search;
using SilverScreen.Views.Popovers;

namespace SilverScreen.Tests;

public sealed class SearchSuggestionServiceTests
{
    [Fact]
    public async Task GetSuggestionsAsync_ReturnsSuggestions_AndCachesResult()
    {
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("client=firefox", request.RequestUri!.Query);
            Assert.Contains("ds=yt", request.RequestUri!.Query);
            Assert.Contains("q=test%20query", request.RequestUri!.Query);
            return Task.FromResult(JsonResponse("""
                                                ["test query", ["test query 1", "test query 2", "test query 3"]]
                                                """));
        });
        using var client = new HttpClient(handler);
        using var service = new YouTubeSearchSuggestionService(client);

        var first = await service.GetSuggestionsAsync("test query");
        var cached = await service.GetSuggestionsAsync("test query");

        Assert.Equal(3, first.Count);
        Assert.Equal("test query 1", first[0]);
        Assert.Equal("test query 2", first[1]);
        Assert.Equal("test query 3", first[2]);
        Assert.Same(first, cached);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetSuggestionsAsync_ParsesJsonpFormat_Correctly()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse("""
                                         window.google.ac.h(["test", [["test match", 0, [512]], ["test cricket", 0, [512]]], {"k": 1}])
                                         """)));
        using var client = new HttpClient(handler);
        using var service = new YouTubeSearchSuggestionService(client);

        var results = await service.GetSuggestionsAsync("test");

        Assert.Equal(2, results.Count);
        Assert.Equal("test match", results[0]);
        Assert.Equal("test cricket", results[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task GetSuggestionsAsync_IgnoresEmptyOrWhitespaceQueries(string? query)
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse("[]")));
        using var client = new HttpClient(handler);
        using var service = new YouTubeSearchSuggestionService(client);

        var results = await service.GetSuggestionsAsync(query!);

        Assert.Empty(results);
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("http://youtu.be/dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/channel/UC12345")]
    public async Task GetSuggestionsAsync_IgnoresYouTubeUrls(string url)
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse("[]")));
        using var client = new HttpClient(handler);
        using var service = new YouTubeSearchSuggestionService(client);

        var results = await service.GetSuggestionsAsync(url);

        Assert.Empty(results);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task GetSuggestionsAsync_HandlesHttpErrorGracefully()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        using var client = new HttpClient(handler);
        using var service = new YouTubeSearchSuggestionService(client);

        var results = await service.GetSuggestionsAsync("error query");

        Assert.Empty(results);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetSuggestionsAsync_HandlesMalformedJsonGracefully()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse("invalid json {{{")));
        using var client = new HttpClient(handler);
        using var service = new YouTubeSearchSuggestionService(client);

        var results = await service.GetSuggestionsAsync("malformed query");

        Assert.Empty(results);
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData("lo-fi", "lo-fi hip hop", "lo-fi<b> hip hop</b>")]
    [InlineData("LO-FI", "lo-fi hip hop", "lo-fi<b> hip hop</b>")]
    [InlineData("c#", "c# & .net <core>", "c#<b> &amp; .net &lt;core&gt;</b>")]
    [InlineData("piano", "guitar songs", "guitar songs")]
    [InlineData("", "test suggestion", "test suggestion")]
    [InlineData("   ", "test suggestion", "test suggestion")]
    public void FormatSuggestionMarkup_FormatsPrefixAndBoldsSuffix(string query, string suggestion, string expected)
    {
        var formatted = SearchPopoverView.FormatSuggestionMarkup(query, suggestion);
        Assert.Equal(expected, formatted);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
    }

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return handler(request, cancellationToken);
        }
    }
}