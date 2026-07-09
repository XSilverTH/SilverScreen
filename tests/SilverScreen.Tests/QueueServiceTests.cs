using SilverScreen.Core.Models;
using SilverScreen.Services;

namespace SilverScreen.Tests;

public sealed class QueueServiceTests
{
    [Fact]
    public void TotalDurationSumsQueuedVideos()
    {
        var service = new QueueService();

        service.Add(CreateVideo("one", TimeSpan.FromMinutes(2)));
        service.Add(CreateVideo("two", TimeSpan.FromSeconds(45)));

        Assert.Equal(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(45), service.TotalDuration);
        Assert.Equal(2, service.Items.Count);
    }

    private static VideoSummary CreateVideo(string id, TimeSpan duration)
    {
        return new VideoSummary(id, $"Video {id}", "Test Channel", duration, "placeholder://test", false);
    }
}
