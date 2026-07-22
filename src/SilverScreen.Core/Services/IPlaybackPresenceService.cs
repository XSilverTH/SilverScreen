using SilverScreen.Core.Models;

namespace SilverScreen.Core.Services;

public interface IPlaybackPresenceService : IDisposable
{
    void SetPlaying(PlaybackRequest request, DateTimeOffset startedAt);
    void Clear();
}