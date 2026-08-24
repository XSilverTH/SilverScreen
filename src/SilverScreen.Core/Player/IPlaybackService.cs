namespace SilverScreen.Core.Player;

public interface IPlaybackService
{
    Task<string> PlayAsync(PlaybackRequest request);
}