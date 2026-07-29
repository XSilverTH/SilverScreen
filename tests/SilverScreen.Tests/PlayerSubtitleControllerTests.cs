using SilverScreen.Views.Player;

namespace SilverScreen.Tests;

public sealed class PlayerSubtitleControllerTests
{
    [Theory]
    [InlineData("en", "en", true)]
    [InlineData("EN", "en", true)]
    [InlineData("en-US", "en-US", true)]
    [InlineData("en-US", "en-GB", true)]
    [InlineData("en", "en-US", true)]
    [InlineData("en-US", "en", true)]
    [InlineData("eng", "en", false)]
    [InlineData("fr-FR", "en", false)]
    [InlineData("", "en", false)]
    [InlineData("en", "", false)]
    [InlineData("   ", "en", false)]
    public void SubtitleLanguageMatches_ComparesExactOrBaseLanguage(string language, string preferredLanguage,
        bool expected)
    {
        Assert.Equal(expected, PlayerSubtitleController.SubtitleLanguageMatches(language, preferredLanguage));
    }
}