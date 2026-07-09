using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Mock;

public sealed class MockPlaybackService : IPlaybackService
{
    public Task<string> PlayAsync(PlaybackRequest request) => Task.FromResult($"Play stub: {request.Title}");
}
