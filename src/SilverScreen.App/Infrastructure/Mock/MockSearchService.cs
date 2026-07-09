using System;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Mock;

public sealed class MockSearchService : ISearchService
{
    public FeedPage Search(SearchRequest request)
    {
        return FeedPage.Empty;
    }

    public bool IsLikelyYouTubeUrl(string text)
    {
        return Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase));
    }
}
