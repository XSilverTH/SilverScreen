namespace SilverScreen.Core.Browsing.Common;

public sealed record YouTubeVideoDetailsResult(
    YouTubeVideoDetails? Details,
    bool IsSuccess,
    string StatusMessage);