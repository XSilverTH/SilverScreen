using SilverScreen.Core.Models;

namespace SilverScreen.Infrastructure.YouTube;

public sealed record HistoryFeedResult(
    IReadOnlyList<VideoSummary> Videos,
    string? ContinuationToken,
    bool IsSuccess,
    string? StatusMessage,
    bool RequiresAuthentication);