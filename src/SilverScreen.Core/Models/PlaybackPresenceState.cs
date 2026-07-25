namespace SilverScreen.Core.Models;

public sealed record PlaybackPresenceState(
    int PlaylistIndex,
    TimeSpan Position,
    TimeSpan Duration,
    bool IsPaused,
    double Speed,
    DateTimeOffset ObservedAt)
{
    public static PlaybackPresenceState CreateInitial(DateTimeOffset observedAt)
    {
        return new PlaybackPresenceState(0, TimeSpan.Zero, TimeSpan.Zero, true, 1, observedAt);
    }
}
