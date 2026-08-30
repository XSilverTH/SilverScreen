using SilverScreen.Core.Browsing.Common;
using ApiPlaybackProgress = YoutubeAPI.Models.Videos.VideoPlaybackProgress;

namespace SilverScreen.Infrastructure.YouTube;

internal static class YouTubePlaybackProgressMapper
{
    public static YouTubePlaybackProgress? Map(ApiPlaybackProgress? progress)
    {
        return progress is null
            ? null
            : new YouTubePlaybackProgress(progress.WatchedFraction, progress.ResumePosition, progress.IsCompleted);
    }
}
