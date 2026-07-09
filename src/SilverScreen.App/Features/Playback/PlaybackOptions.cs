namespace SilverScreen.Features.Playback;

public sealed class PlaybackOptions
{
    public string MpvExecutablePath { get; init; } = "mpv";

    public bool ExternalMpvEnabled { get; init; } = true;
}
