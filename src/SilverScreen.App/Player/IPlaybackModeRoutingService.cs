using SilverScreen.Core.Player;

namespace SilverScreen.Player;

/// <summary>
///     Extended playback interface exposing embedded player media presence and playback state.
/// </summary>
public interface IPlaybackModeRoutingService : IPlaybackService
{
    bool HasMedia { get; }
    bool IsPaused { get; }
    Task TogglePauseAsync();
    event EventHandler? PlaybackStateChanged;
}
