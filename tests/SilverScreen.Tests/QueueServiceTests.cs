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
    public void AddingTheSameVideoTwiceCreatesIndependentEntries()
    {
        var service = new QueueService();
        var video = CreateVideo("duplicate", TimeSpan.FromMinutes(1));

        var first = service.Add(video);
        var second = service.Add(video);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal([first.Id, second.Id], service.Items.Select(item => item.Id));
    }

    [Fact]
    public void MoveReordersByStableIdAndRaisesOneChange()
    {
        var service = new QueueService();
        var first = service.Add(CreateVideo("first", TimeSpan.FromMinutes(1)));
        var second = service.Add(CreateVideo("second", TimeSpan.FromMinutes(1)));
        var third = service.Add(CreateVideo("third", TimeSpan.FromMinutes(1)));
        var changes = 0;
        service.Changed += (_, _) => changes++;

        Assert.True(service.Move(first.Id, 2));
        Assert.Equal([second.Id, third.Id, first.Id], service.Items.Select(item => item.Id));
        Assert.Equal(1, changes);

        Assert.True(service.Move(first.Id, 0));
        Assert.Equal([first.Id, second.Id, third.Id], service.Items.Select(item => item.Id));
        Assert.Equal(2, changes);
    }

    [Fact]
    public void InvalidOrNoOpMovesDoNotRaiseChange()
    {
        var service = new QueueService();
        var item = service.Add(CreateVideo("one", TimeSpan.FromMinutes(1)));
        var changes = 0;
        service.Changed += (_, _) => changes++;

        Assert.False(service.Move(Guid.NewGuid(), 0));
        Assert.False(service.Move(item.Id, -1));
        Assert.False(service.Move(item.Id, 1));
        Assert.False(service.Move(item.Id, 0));
        Assert.Equal(0, changes);
    }

    [Fact]
    public void AddNextRemoveAndClearMaintainOrderedQueue()
    {
        var service = new QueueService();
        var first = service.Add(CreateVideo("first", TimeSpan.FromMinutes(1)));
        var next = service.AddNext(CreateVideo("next", TimeSpan.FromMinutes(2)));
        var changes = 0;
        service.Changed += (_, _) => changes++;

        Assert.Equal([next.Id, first.Id], service.Items.Select(item => item.Id));
        Assert.True(service.Remove(first.Id));
        Assert.False(service.Remove(first.Id));
        Assert.Equal(1, changes);

        service.Clear();
        Assert.Empty(service.Items);
        Assert.Equal(2, changes);
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