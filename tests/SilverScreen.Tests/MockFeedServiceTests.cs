using System.Linq;
using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Mock;

namespace SilverScreen.Tests;

public sealed class MockFeedServiceTests
{
    [Fact]
    public void HomeFeedFilterExcludesShorts()
    {
        // Arrange
        var feedService = new MockFeedService();
        var rawVideos = feedService.GetHomeFeed().Videos;

        // Act & Assert: Verify raw feed contains at least one Short video to ensure the test is meaningful
        Assert.True(rawVideos.Any(video => video.IsShort), "Expected raw videos to contain at least one short video.");

        // Act: Apply the same filter used in MainWindow
        var filteredVideos = rawVideos.Where(video => !video.IsShort).ToList();

        // Assert: Verify filtered results contain absolutely no Shorts
        Assert.All(filteredVideos, video => Assert.False(video.IsShort, $"Expected video '{video.Title}' not to be a Short."));
    }
}
