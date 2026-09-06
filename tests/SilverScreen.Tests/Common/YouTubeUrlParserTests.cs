using SilverScreen.Core.Common;

namespace SilverScreen.Tests.Common;

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
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ?t=10s")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ/")]
    [InlineData("https://m.youtube.com/shorts/dQw4w9WgXcQ")]
    [InlineData("https://music.youtube.com/shorts/dQw4w9WgXcQ?si=abc123_-xy")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ?rel=0")]
    [InlineData("https://www.youtube.com/v/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/live/dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?t=42")]
    [InlineData("https://youtu.be/shorts/dQw4w9WgXcQ")]
    public void Parse_ResolvesPlayableVariantsToVideoIdWithWatchUrl(string input)
    {
        var result = YouTubeUrlParser.Parse(input);

        Assert.Equal("dQw4w9WgXcQ", result.VideoId);
        Assert.True(result.Kind is YouTubeUrlKind.Video or YouTubeUrlKind.Shorts,
            $"Expected a playable video kind but got {result.Kind} for {input}.");
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", result.CanonicalWatchUrl);
    }

    [Theory]
    [InlineData("https://www.youtube.com/@somehandle", YouTubeUrlKind.Channel)]
    [InlineData("https://www.youtube.com/channel/UC_x5XG1OV2P6uZZ5FSM9Ttw", YouTubeUrlKind.Channel)]
    [InlineData("https://www.youtube.com/user/SomeUser", YouTubeUrlKind.Channel)]
    [InlineData("https://www.youtube.com/playlist?list=PL123", YouTubeUrlKind.Playlist)]
    [InlineData("https://www.youtube.com/shorts/not-a-valid-id!!", YouTubeUrlKind.Invalid)]
    [InlineData("https://www.youtube.com/watch", YouTubeUrlKind.UnknownYouTube)]
    public void Parse_ClassifiesNonVideoUrlsDistinctlyFromVideo(string input, YouTubeUrlKind expectedKind)
    {
        var result = YouTubeUrlParser.Parse(input);

        Assert.Equal(expectedKind, result.Kind);
        Assert.Null(result.VideoId);
        Assert.Null(result.CanonicalWatchUrl);
    }
}