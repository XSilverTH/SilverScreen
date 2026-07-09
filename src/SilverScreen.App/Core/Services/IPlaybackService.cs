using SilverScreen.Core.Models;

namespace SilverScreen.Core.Services;

public interface IPlaybackService
{
    string Play(PlaybackRequest request);
}
