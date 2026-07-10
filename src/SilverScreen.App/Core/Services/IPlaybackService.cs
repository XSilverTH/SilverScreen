using SilverScreen.Core.Models;

namespace SilverScreen.Core.Services;

public interface IPlaybackService
{
    Task<string> PlayAsync(PlaybackRequest request);
}