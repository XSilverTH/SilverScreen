namespace SilverScreen.Core.Domain;

public sealed record YouTubeVideoDetailsResult(
    YouTubeVideoDetails? Details,
    bool IsSuccess,
    string StatusMessage);