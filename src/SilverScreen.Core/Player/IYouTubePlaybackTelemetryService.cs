namespace SilverScreen.Core.Player;

/// <summary>Reports real playback progress to YouTube for recommendation and history signals.</summary>
public interface IYouTubePlaybackTelemetryService : IDisposable
{
    /// <summary>Begins a telemetry session for one player instance.</summary>
    IYouTubePlaybackTelemetrySession Start(PlaybackRequest request);
}

/// <summary>Accepts state and queue changes from one player instance until that player stops.</summary>
public interface IYouTubePlaybackTelemetrySession : IDisposable
{
    /// <summary>Updates the immutable queue snapshot and identifies the entry currently playing.</summary>
    void UpdateQueue(PlaybackRequest request, int currentIndex);

    void UpdateState(PlaybackPresenceState state);
}