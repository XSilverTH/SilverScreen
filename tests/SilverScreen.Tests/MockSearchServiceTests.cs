using SilverScreen.Infrastructure.Mock;

namespace SilverScreen.Tests;

public sealed class MockSearchServiceTests
{
    [Theory]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", true)]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", true)]
    [InlineData("http://youtube.com/watch?v=dQw4w9WgXcQ", true)]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ", true)]
    [InlineData("HTTPS://YOUTU.BE/dQw4w9WgXcQ", true)]
    [InlineData("hello world", false)]
    [InlineData("youtube.com/watch?v=dQw4w9WgXcQ", false)]
    [InlineData("https://google.com", false)]
    [InlineData("ftp://youtube.com/watch?v=dQw4w9WgXcQ", false)]
    [InlineData("https://notyoutube.com", false)]
    [InlineData("https://youtube.com.attacker.com", false)]
    [InlineData("", false)]
    public void IsLikelyYouTubeUrl_DetectsAndRejectsCorrectly(string input, bool expected)
    {
        // Arrange
        var service = new MockSearchService();

        // Act
        var result = service.IsLikelyYouTubeUrl(input);

        // Assert
        Assert.Equal(expected, result);
    }
}