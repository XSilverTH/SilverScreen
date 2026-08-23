namespace SilverScreen.Core.Models;

public sealed record YouTubeVideoDetailsResult(
    YouTubeVideoDetails? Details,
    bool IsSuccess,
    string StatusMessage);