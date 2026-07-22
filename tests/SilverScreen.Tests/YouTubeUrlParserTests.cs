using SilverScreen.Infrastructure.Features.Search;

namespace SilverScreen.Tests;

public sealed class YouTubeUrlParserTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", YouTubeUrlKind.Video, "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ", YouTubeUrlKind.Shorts, "dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/playlist?list=PL123", YouTubeUrlKind.Playlist, null)]
    [InlineData("https://youtube.com.attacker.com/watch?v=dQw4w9WgXcQ", YouTubeUrlKind.NotYouTube, null)]
    [InlineData("https://youtube.com/watch?v=too_long_video_id", YouTubeUrlKind.Invalid, null)]
    public void Parse_ClassifiesRepresentativeInputs(string input, YouTubeUrlKind expectedKind,
        string? expectedVideoId)
    {
        var result = YouTubeUrlParser.Parse(input);

        Assert.Equal(expectedKind, result.Kind);
        Assert.Equal(expectedVideoId, result.VideoId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://")]
    public void Parse_MalformedInputDoesNotThrow(string? input)
    {
        var exception = Record.Exception(() => YouTubeUrlParser.Parse(input));

        Assert.Null(exception);
    }
}