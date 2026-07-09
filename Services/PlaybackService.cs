using SilverScreen.Core.Models;

namespace SilverScreen.Services;

public sealed class PlaybackService
{
    public string Play(VideoSummary video) => $"Play stub: {video.Title}";
}
