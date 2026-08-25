using SilverScreen.Core.Browsing.Common;

namespace SilverScreen.Core.Browsing.Search;

public sealed record SearchRequest(string Query, int StartIndex = 1, int Count = VideoFeedConstants.DefaultPageSize);