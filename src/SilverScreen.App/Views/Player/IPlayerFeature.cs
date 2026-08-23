using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Features.Playback;

namespace SilverScreen.Views.Player;

internal interface IPlayerFeature : IDisposable
{
    void Load(VideoSummary video);
    void UpdatePlayback(LibMpvPlaybackState state, string videoId);
    void Clear();
}
