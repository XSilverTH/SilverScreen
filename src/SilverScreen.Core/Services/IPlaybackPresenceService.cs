using SilverScreen.Core.Models;

namespace SilverScreen.Core.Services;

public interface IPlaybackPresenceService : IDisposable
{
    void SetPlaybackState(PlaybackRequest request, PlaybackPresenceState state);
    void Clear();
}