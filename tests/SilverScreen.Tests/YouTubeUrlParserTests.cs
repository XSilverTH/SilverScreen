using SilverScreen.Features.Search;

namespace SilverScreen.Tests;

public sealed class YouTubeUrlParserTests
{
    [Theory]
    // 1. Standard watch URL as Video
    [InlineData("standard HTTPS watch www", "https://www.youtube.com/watch?v=dQw4w9WgXcQ", YouTubeUrlKind.Video,
        "dQw4w9WgXcQ", null, null, "https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("standard HTTP watch www", "http://www.youtube.com/watch?v=dQw4w9WgXcQ", YouTubeUrlKind.Video,
        "dQw4w9WgXcQ", null, null, "https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("standard watch no-www", "https://youtube.com/watch?v=dQw4w9WgXcQ", YouTubeUrlKind.Video, "dQw4w9WgXcQ",
        null, null, "https://www.youtube.com/watch?v=dQw4w9WgXcQ")]

    // 2. youtu.be as Video
    [InlineData("youtu.be short link HTTPS", "https://youtu.be/dQw4w9WgXcQ", YouTubeUrlKind.Video, "dQw4w9WgXcQ", null,
        null, "https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("youtu.be short link HTTP", "http://youtu.be/dQw4w9WgXcQ", YouTubeUrlKind.Video, "dQw4w9WgXcQ", null,
        null, "https://www.youtube.com/watch?v=dQw4w9WgXcQ")]

    // 3. m.youtube.com watch as Video
    [InlineData("mobile site watch URL", "https://m.youtube.com/watch?v=dQw4w9WgXcQ", YouTubeUrlKind.Video,
        "dQw4w9WgXcQ", null, null, "https://www.youtube.com/watch?v=dQw4w9WgXcQ")]

    // 4. Extra query params preserving only video id
    [InlineData("watch with extra params at end", "https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=10s&feature=emb_title",
        YouTubeUrlKind.Video, "dQw4w9WgXcQ", null, null, "https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("watch with extra params in front", "https://m.youtube.com/watch?feature=emb_title&v=dQw4w9WgXcQ&t=10s",
        YouTubeUrlKind.Video, "dQw4w9WgXcQ", null, null, "https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("youtu.be with query params", "https://youtu.be/dQw4w9WgXcQ?t=10s&feature=share", YouTubeUrlKind.Video,
        "dQw4w9WgXcQ", null, null, "https://www.youtube.com/watch?v=dQw4w9WgXcQ")]

    // 5. Shorts as Shorts
    [InlineData("Shorts www", "https://www.youtube.com/shorts/dQw4w9WgXcQ", YouTubeUrlKind.Shorts, "dQw4w9WgXcQ", null,
        null, null)]
    [InlineData("Shorts no-www", "https://youtube.com/shorts/dQw4w9WgXcQ", YouTubeUrlKind.Shorts, "dQw4w9WgXcQ", null,
        null, null)]
    [InlineData("Shorts mobile", "https://m.youtube.com/shorts/dQw4w9WgXcQ", YouTubeUrlKind.Shorts, "dQw4w9WgXcQ", null,
        null, null)]

    // 6. /channel, /c, /@handle as Channel
    [InlineData("Channel path", "https://www.youtube.com/channel/UCBR8-gII631gBw5hZTtRY-g", YouTubeUrlKind.Channel,
        null, "channel/UCBR8-gII631gBw5hZTtRY-g", null, null)]
    [InlineData("Custom URL c path", "https://youtube.com/c/YouTubeCreators", YouTubeUrlKind.Channel, null,
        "c/YouTubeCreators", null, null)]
    [InlineData("Handle path", "https://m.youtube.com/@YouTube", YouTubeUrlKind.Channel, null, "@YouTube", null, null)]

    // 7. Playlist as Playlist
    [InlineData("Playlist HTTPS", "https://www.youtube.com/playlist?list=PL4fGSI1pDJn5kI9Fh3spxo68j8z8b74x8",
        YouTubeUrlKind.Playlist, null, null, "PL4fGSI1pDJn5kI9Fh3spxo68j8z8b74x8", null)]
    [InlineData("Playlist with extra params",
        "https://youtube.com/playlist?list=PL4fGSI1pDJn5kI9Fh3spxo68j8z8b74x8&si=some-share-id",
        YouTubeUrlKind.Playlist, null, null, "PL4fGSI1pDJn5kI9Fh3spxo68j8z8b74x8", null)]

    // 8. Plain text NotYouTube
    [InlineData("Plain text words", "hello world", YouTubeUrlKind.NotYouTube, null, null, null, null)]
    [InlineData("Video ID like plain text", "dQw4w9WgXcQ", YouTubeUrlKind.NotYouTube, null, null, null, null)]
    [InlineData("Non-YouTube URL", "https://google.com", YouTubeUrlKind.NotYouTube, null, null, null, null)]
    [InlineData("Non-YouTube watch URL style", "http://example.com/watch?v=dQw4w9WgXcQ", YouTubeUrlKind.NotYouTube,
        null, null, null, null)]

    // 10. Attacker hosts rejected
    [InlineData("Attacker suffix", "https://youtube.com.attacker.com/watch?v=dQw4w9WgXcQ", YouTubeUrlKind.NotYouTube,
        null, null, null, null)]
    [InlineData("Attacker suffix 2", "https://www.youtube.com.evil.com/", YouTubeUrlKind.NotYouTube, null, null, null,
        null)]
    [InlineData("Attacker subdomain", "https://evil.youtube.com.evil.com/watch?v=dQw4w9WgXcQ",
        YouTubeUrlKind.NotYouTube, null, null, null, null)]
    [InlineData("Attacker hyphen host", "https://youtube.com-evil.com/", YouTubeUrlKind.NotYouTube, null, null, null,
        null)]
    [InlineData("Attacker path only", "https://evil.com/youtube.com/watch?v=dQw4w9WgXcQ", YouTubeUrlKind.NotYouTube,
        null, null, null, null)]

    // 11. Non-http/https rejected
    [InlineData("FTP scheme YouTube host", "ftp://youtube.com/watch?v=dQw4w9WgXcQ", YouTubeUrlKind.Invalid, null, null,
        null, null)]
    [InlineData("RTSP scheme youtu.be host", "rtsp://youtu.be/dQw4w9WgXcQ", YouTubeUrlKind.Invalid, null, null, null,
        null)]
    [InlineData("File scheme", "file:///watch?v=dQw4w9WgXcQ", YouTubeUrlKind.NotYouTube, null, null, null, null)]

    // Other cases: Invalid / UnknownYouTube
    [InlineData("Invalid VideoId on watch", "https://www.youtube.com/watch?v=too_long_video_id", YouTubeUrlKind.Invalid,
        null, null, null, null)]
    [InlineData("Invalid VideoId on youtu.be", "https://youtu.be/too_short", YouTubeUrlKind.Invalid, null, null, null,
        null)]
    [InlineData("Invalid VideoId on shorts", "https://youtube.com/shorts/invalid_chars!", YouTubeUrlKind.Invalid, null,
        null, null, null)]
    [InlineData("Unknown watch without v param",
        "https://www.youtube.com/watch?list=PL4fGSI1pDJn5kI9Fh3spxo68j8z8b74x8", YouTubeUrlKind.UnknownYouTube, null,
        null, null, null)]
    [InlineData("Unknown path segment on YouTube", "https://youtube.com/unknown_segment", YouTubeUrlKind.UnknownYouTube,
        null, null, null, null)]
    [InlineData("Unknown empty youtu.be path", "https://youtu.be/", YouTubeUrlKind.UnknownYouTube, null, null, null,
        null)]
    [InlineData("Unknown playlist without list param", "https://youtube.com/playlist", YouTubeUrlKind.UnknownYouTube,
        null, null, null, null)]
    public void Parse_ParsesUrlsCorrectly(
        string caseName,
        string? input,
        YouTubeUrlKind expectedKind,
        string? expectedVideoId,
        string? expectedChannelPath,
        string? expectedPlaylistId,
        string? expectedCanonicalWatchUrl)
    {
        Assert.NotNull(caseName);

        // Act
        var result = YouTubeUrlParser.Parse(input);

        // Assert
        Assert.Equal(expectedKind, result.Kind);
        Assert.Equal(expectedVideoId, result.VideoId);
        Assert.Equal(expectedChannelPath, result.ChannelPath);
        Assert.Equal(expectedPlaylistId, result.PlaylistId);
        Assert.Equal(expectedCanonicalWatchUrl, result.CanonicalWatchUrl);
    }

    [Theory]
    // 9. Malformed input no throw
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://")]
    [InlineData("https://")]
    [InlineData("http://[invalid-ipv6]")]
    [InlineData("http://[::1")]
    [InlineData("https://youtube.com:invalid_port/watch?v=dQw4w9WgXcQ")]
    public void Parse_MalformedInput_DoesNotThrow(string? input)
    {
        // Act
        var exception = Record.Exception(() => YouTubeUrlParser.Parse(input));

        // Assert
        Assert.Null(exception);

        var result = YouTubeUrlParser.Parse(input);
        Assert.True(result.Kind == YouTubeUrlKind.NotYouTube || result.Kind == YouTubeUrlKind.Invalid);
    }
}