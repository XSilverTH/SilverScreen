using SilverScreen.Core.Common;
using Xunit.Sdk;

namespace SilverScreen.Tests.Common;

public sealed class YouTubeUrlParserTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", typeof(YouTubeUrlParseResult.Video), "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ", typeof(YouTubeUrlParseResult.Shorts), "dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/playlist?list=PL123", typeof(YouTubeUrlParseResult.Playlist), "PL123")]
    [InlineData("https://youtube.com.attacker.com/watch?v=dQw4w9WgXcQ", typeof(YouTubeUrlParseResult.NotYouTube), null)]
    [InlineData("https://youtube.com/watch?v=too_long_video_id", typeof(YouTubeUrlParseResult.Invalid), null)]
    public void Parse_ClassifiesRepresentativeInputs(string input, Type expectedCaseType, string? expectedPayload)
    {
        var result = YouTubeUrlParser.Parse(input);

        Assert.IsType(expectedCaseType, result.Value);
        var actualPayload = result switch
        {
            YouTubeUrlParseResult.Video(var videoId) => videoId,
            YouTubeUrlParseResult.Shorts(var videoId) => videoId,
            YouTubeUrlParseResult.Playlist(var playlistId) => playlistId,
            YouTubeUrlParseResult.Channel(var channelPath) => channelPath,
            YouTubeUrlParseResult.NotYouTube => null,
            YouTubeUrlParseResult.UnknownYouTube => null,
            YouTubeUrlParseResult.Invalid => null
        };
        Assert.Equal(expectedPayload, actualPayload);
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

        var videoId = result switch
        {
            YouTubeUrlParseResult.Video(var id) => id,
            YouTubeUrlParseResult.Shorts(var id) => id,
            _ => throw new XunitException($"Expected a playable video case but got {result.Value} for {input}.")
        };
        Assert.Equal("dQw4w9WgXcQ", videoId);
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", result.CanonicalWatchUrl);
    }

    [Theory]
    [InlineData("https://www.youtube.com/@somehandle", typeof(YouTubeUrlParseResult.Channel))]
    [InlineData("https://www.youtube.com/channel/UC_x5XG1OV2P6uZZ5FSM9Ttw", typeof(YouTubeUrlParseResult.Channel))]
    [InlineData("https://www.youtube.com/user/SomeUser", typeof(YouTubeUrlParseResult.Channel))]
    [InlineData("https://www.youtube.com/playlist?list=PL123", typeof(YouTubeUrlParseResult.Playlist))]
    [InlineData("https://www.youtube.com/shorts/not-a-valid-id!!", typeof(YouTubeUrlParseResult.Invalid))]
    [InlineData("https://www.youtube.com/watch", typeof(YouTubeUrlParseResult.UnknownYouTube))]
    public void Parse_ClassifiesNonVideoUrlsDistinctlyFromVideo(string input, Type expectedCaseType)
    {
        var result = YouTubeUrlParser.Parse(input);

        Assert.IsType(expectedCaseType, result.Value);
        Assert.Null(result.CanonicalWatchUrl);
    }
}