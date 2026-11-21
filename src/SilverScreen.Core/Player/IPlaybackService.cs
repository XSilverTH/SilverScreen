namespace SilverScreen.Core.Player;

public interface IPlaybackService
{
    Task<string> PlayAsync(PlaybackRequest request);
    Task<string> PlayAlternateAsync(PlaybackRequest request) => PlayAsync(request);
}
