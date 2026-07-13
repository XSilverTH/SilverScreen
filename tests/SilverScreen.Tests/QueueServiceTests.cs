using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Features.Queue;

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

    [Fact]
    public void AddingToQueueIncreasesItemCountAndPreservesVideo()
    {
        // Arrange
        var service = new QueueService();
        var video = CreateVideo("test-id", TimeSpan.FromMinutes(3));

        // Act
        var item = service.Add(video);

        // Assert
        Assert.Single(service.Items);
        Assert.Equal(video, service.Items[0].Video);
        Assert.Equal(video, item.Video);
    }

    [Fact]
    public void ClearingNonEmptyQueueRemovesAllItemsAndResetsTotalDurationToZero()
    {
        // Arrange
        var service = new QueueService();
        service.Add(CreateVideo("one", TimeSpan.FromMinutes(2)));
        service.Add(CreateVideo("two", TimeSpan.FromSeconds(45)));

        Assert.Equal(2, service.Items.Count);
        Assert.Equal(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(45), service.TotalDuration);

        // Act
        service.Clear();

        // Assert
        Assert.Empty(service.Items);
        Assert.Equal(TimeSpan.Zero, service.TotalDuration);
    }

    private static VideoSummary CreateVideo(string id, TimeSpan duration)
    {
        return new VideoSummary(id, $"Video {id}", "Test Channel", duration, "placeholder://test", false);
    }
}