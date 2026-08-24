namespace SilverScreen.Core.Player;

public interface ISponsorBlockService
{
    Task<IReadOnlyList<SponsorBlockSegment>> GetSegmentsAsync(string videoId,
        IReadOnlyCollection<string> categories, CancellationToken cancellationToken = default);
}