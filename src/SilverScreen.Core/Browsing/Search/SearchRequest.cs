using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Core.Browsing.Search;

public sealed record SearchRequest(
    string Query,
    int Count = VideoFeedConstants.DefaultPageSize,
    string? ContinuationToken = null);