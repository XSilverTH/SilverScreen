using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Search;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Queue;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.YouTube;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Infrastructure.Player.Comments;
using SilverScreen.Infrastructure.Browsing.Common;
using SilverScreen.Infrastructure.Browsing.Home;
using SilverScreen.Infrastructure.Browsing.Channel;
using SilverScreen.Infrastructure.Browsing.Search;
using SilverScreen.Infrastructure.Browsing.History;
using SilverScreen.Infrastructure.Queue;
using SilverScreen.Infrastructure.Account.Session;
using SilverScreen.Infrastructure.Account.Auth;
using SilverScreen.Infrastructure.Account.Profile;
using SilverScreen.Infrastructure.Preferences;
using SilverScreen.Shell;
using SilverScreen.Browsing.Components;
using SilverScreen.Browsing.Home;
using SilverScreen.Browsing.Channel;
using SilverScreen.Browsing.Search;
using SilverScreen.Browsing.History;
using SilverScreen.Player;
using SilverScreen.Player.Views;
using SilverScreen.Player.Controllers;
using SilverScreen.Player.Comments;
using SilverScreen.Queue;
using SilverScreen.Account.Profile;
using SilverScreen.Account.Auth;
using SilverScreen.Account.Session;
using SilverScreen.Preferences;

using System.Net;

namespace SilverScreen.Tests.Browsing.Search;

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
