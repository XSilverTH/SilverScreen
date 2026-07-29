using SilverScreen.Core.Models;

namespace SilverScreen.Core.Services;

public interface ISponsorBlockService
{
    Task<IReadOnlyList<SponsorBlockSegment>> GetSegmentsAsync(string videoId,
        IReadOnlyCollection<string> categories, CancellationToken cancellationToken = default);
}