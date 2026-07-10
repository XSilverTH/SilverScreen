using System.Collections.Generic;
using SilverScreen.Core.Models;

namespace SilverScreen.Infrastructure.YouTube;

public sealed record HomeFeedResult(
    IReadOnlyList<VideoSummary> Videos,
    string? ContinuationToken,
    bool IsSuccess,
    string? StatusMessage,
    bool RequiresAuthentication);