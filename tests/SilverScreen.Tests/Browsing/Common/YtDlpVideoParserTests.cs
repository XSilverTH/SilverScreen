using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Search;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Queue;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.YouTube;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Infrastructure.Player.Comments;
using SilverScreen.Infrastructure.Browsing.Common;
using SilverScreen.Infrastructure.Browsing.Home;
using SilverScreen.Infrastructure.Browsing.Channel;
using SilverScreen.Infrastructure.Browsing.Search;
using SilverScreen.Infrastructure.Browsing.History;
using SilverScreen.Infrastructure.Queue;
using SilverScreen.Infrastructure.Account.Session;
using SilverScreen.Infrastructure.Account.Auth;
using SilverScreen.Infrastructure.Account.Profile;
using SilverScreen.Infrastructure.Preferences;
using SilverScreen.Shell;
using SilverScreen.Browsing.Components;
using SilverScreen.Browsing.Home;
using SilverScreen.Browsing.Channel;
using SilverScreen.Browsing.Search;
using SilverScreen.Browsing.History;
using SilverScreen.Player;
using SilverScreen.Player.Views;
using SilverScreen.Player.Controllers;
using SilverScreen.Player.Comments;
using SilverScreen.Queue;
using SilverScreen.Account.Profile;
using SilverScreen.Account.Auth;
using SilverScreen.Account.Session;
using SilverScreen.Preferences;


namespace SilverScreen.Tests.Browsing.Common;

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

    [Fact]
    public void ParseDetails_ExtractsDescriptionViewsPublicationAndChannel()
    {
        const string output = """
                              {
                                "title": "A detailed video",
                                "uploader": "Example channel",
                                "description": "The full description.",
                                "view_count": 1234567,
                                "timestamp": 1700000000
                              }
                              """;

        var details = YtDlpVideoParser.ParseDetails(output);

        Assert.Equal("The full description.", details.Description);
        Assert.Equal(1_234_567, details.ViewCount);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), details.PublishedAt);
        Assert.Equal("A detailed video", details.Title);
        Assert.Equal("Example channel", details.ChannelName);
    }
}
