namespace SilverScreen.Core.Player;

/// <summary>Reports real playback progress to YouTube for recommendation and history signals.</summary>
public interface IYouTubePlaybackTelemetryService : IDisposable
{
    /// <summary>Begins a telemetry session for one player instance.</summary>
    IYouTubePlaybackTelemetrySession Start(PlaybackRequest request);
}

/// <summary>Accepts state changes from one player instance until that player stops.</summary>
public interface IYouTubePlaybackTelemetrySession : IDisposable
{
    void UpdateState(PlaybackPresenceState state);
}