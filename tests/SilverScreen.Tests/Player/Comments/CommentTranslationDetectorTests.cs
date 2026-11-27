using SilverScreen.Core.Player.Comments;

namespace SilverScreen.Tests.Player.Comments;

public sealed class CommentTranslationDetectorTests
{
    [Theory]
    [InlineData("", "en", false)]
    [InlineData("   ", "en", false)]
    [InlineData("26/11/2026 🎉🎵", "en", false)]
    [InlineData("https://example.com/watch?v=123 @user 1:23", "en", false)]
    [InlineData("This is a great video and I love it", "en", false)]
    [InlineData("Esta canción es muy buena, gracias por compartir", "en", true)]
    [InlineData("これは素晴らしい動画ですね", "en", true)]
    [InlineData("これは素晴らしい動画ですね", "ja", false)]
    [InlineData("This is a great video", "ja", true)]
    [InlineData("Привет, это отличное видео!", "en", true)]
    [InlineData("Привет, это отличное видео!", "ru", false)]
    [InlineData("Bonjour tout le monde, c'est très bien!", "en-US", true)]
    [InlineData("Esta canción es muy buena", "es", false)]
    public void NeedsTranslation_DetectsWhetherCommentNeedsTranslation(
        string text,
        string targetLanguage,
        bool expected)
    {
        Assert.Equal(expected, CommentTranslationDetector.NeedsTranslation(text, targetLanguage));
    }
}
