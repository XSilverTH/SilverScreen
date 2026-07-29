using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests;

public sealed class YtDlpVideoParserTests
{
    [Fact]
    public void Parse_ConvertsMixPlaylistsToPlayableRadioUrlsAndSkipsOrdinaryPlaylists()
    {
        const string output = """
                              { "entries": [
                                {
                                  "id": "RDicBDYkfxpMs",
                                  "title": "Mix - Looping the Rooms",
                                  "webpage_url": "https://www.youtube.com/playlist?list=RDicBDYkfxpMs"
                                },
                                {
                                  "id": "RDGMEMXdNDEg4wQ96My0DhjI-cIgVMOSYmTw6_bjc",
                                  "title": "Mix - Music of Japan",
                                  "webpage_url": "https://www.youtube.com/playlist?list=RDGMEMXdNDEg4wQ96My0DhjI-cIgVMOSYmTw6_bjc"
                                },
                                {
                                  "id": "PLrL6Kyqj1HGbhmAaYMIlT4xsz-QTs7sue",
                                  "title": "Ordinary playlist",
                                  "webpage_url": "https://www.youtube.com/playlist?list=PLrL6Kyqj1HGbhmAaYMIlT4xsz-QTs7sue"
                                }
                              ] }
                              """;

        var videos = YtDlpVideoParser.Parse(output);

        var simpleMix = videos[0];
        Assert.Equal("icBDYkfxpMs", simpleMix.Id);
        Assert.Equal(
            "https://www.youtube.com/watch?v=icBDYkfxpMs&list=RDicBDYkfxpMs&start_radio=1",
            simpleMix.WatchUrl);

        var contextualMix = videos[1];
        Assert.Equal("OSYmTw6_bjc", contextualMix.Id);
        Assert.Equal(
            "https://www.youtube.com/watch?v=OSYmTw6_bjc&list=RDGMEMXdNDEg4wQ96My0DhjI-cIgVMOSYmTw6_bjc&start_radio=1",
            contextualMix.WatchUrl);

        Assert.Equal(2, videos.Count);
    }
}