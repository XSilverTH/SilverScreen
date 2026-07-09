using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Mock;

public sealed class MockPlaybackService : IPlaybackService
{
    public string Play(PlaybackRequest request) => $"Play stub: {request.Video.Title}";
}
