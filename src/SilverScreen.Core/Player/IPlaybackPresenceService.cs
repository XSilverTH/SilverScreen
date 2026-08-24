namespace SilverScreen.Core.Player;

public interface IPlaybackPresenceService : IDisposable
{
    void SetPlaybackState(PlaybackRequest request, PlaybackPresenceState state);
    void Clear();
}