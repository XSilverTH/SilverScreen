namespace SilverScreen.Core.Player;

/// <summary>Stores and publishes the furthest locally played position for videos.</summary>
public interface IWatchProgressService
{
    event EventHandler<WatchProgress>? ProgressChanged;

    double? GetFraction(string videoId);

    double? GetResumeFraction(string videoId);

    void Update(PlaybackRequest request, PlaybackPresenceState state);
}