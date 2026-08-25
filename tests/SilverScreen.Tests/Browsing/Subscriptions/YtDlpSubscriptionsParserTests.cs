using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests.Browsing.Subscriptions;

public sealed class YtDlpSubscriptionsParserTests
{
    [Fact]
    public void ParseChannels_ExtractsMetadataFromSingleJson()
    {
        const string output = """
                              {
                                "entries": [
                                  {
                                    "id": "UC_x5XG1OV2P6uZZ5FSM9Ttw",
                                    "title": "Google Developers",
                                    "url": "https://www.youtube.com/channel/UC_x5XG1OV2P6uZZ5FSM9Ttw",
                                    "thumbnail": "https://img.example/google.jpg",
                                    "description": "The official channel for Google developers.",
                                    "channel_follower_count": 2300000
                                  },
                                  {
                                    "id": "UCsBjURrPoezykLs9EqgamOA",
                                    "title": "Fireship",
                                    "url": "https://www.youtube.com/@Fireship",
                                    "thumbnails": [
                                      { "url": "https://img.example/fireship_low.jpg", "preference": 0 },
                                      { "url": "https://img.example/fireship_high.jpg", "preference": 10 }
                                    ],
                                    "subscriber_count": 3000000
                                  }
                                ]
                              }
                              """;

        var channels = YtDlpSubscriptionsParser.ParseChannels(output);

        Assert.Equal(2, channels.Count);

        var first = channels[0];
        Assert.Equal("UC_x5XG1OV2P6uZZ5FSM9Ttw", first.Id);
        Assert.Equal("Google Developers", first.Title);
        Assert.Equal("https://www.youtube.com/channel/UC_x5XG1OV2P6uZZ5FSM9Ttw", first.Url);
        Assert.Equal("https://img.example/google.jpg", first.AvatarUrl);
        Assert.Equal("The official channel for Google developers.", first.Description);
        Assert.Equal(2_300_000, first.SubscriberCount);

        var second = channels[1];
        Assert.Equal("UCsBjURrPoezykLs9EqgamOA", second.Id);
        Assert.Equal("Fireship", second.Title);
        Assert.Equal("https://www.youtube.com/@Fireship", second.Url);
        Assert.Equal("https://img.example/fireship_high.jpg", second.AvatarUrl);
        Assert.Equal(3_000_000, second.SubscriberCount);
    }

    [Fact]
    public void ParseChannels_HandlesNdJson()
    {
        const string output = """
                              {"id": "UC1", "title": "Channel One", "url": "https://www.youtube.com/@one"}
                              {"id": "UC2", "title": "Channel Two", "url": "https://www.youtube.com/@two"}
                              """;

        var channels = YtDlpSubscriptionsParser.ParseChannels(output);

        Assert.Equal(2, channels.Count);
        Assert.Equal("Channel One", channels[0].Title);
        Assert.Equal("Channel Two", channels[1].Title);
    }

    [Fact]
    public void ParseChannels_NormalizesRelativeAndHandleUrls()
    {
        const string output = """
                              {
                                "entries": [
                                  {
                                    "id": "UC_abc",
                                    "title": "Channel UC"
                                  },
                                  {
                                    "id": "@handle",
                                    "title": "Channel Handle"
                                  },
                                  {
                                    "id": "rel1",
                                    "title": "Channel Relative",
                                    "url": "/@relative"
                                  }
                                ]
                              }
                              """;

        var channels = YtDlpSubscriptionsParser.ParseChannels(output);

        Assert.Equal(3, channels.Count);
        Assert.Equal("https://www.youtube.com/channel/UC_abc", channels[0].Url);
        Assert.Equal("https://www.youtube.com/@handle", channels[1].Url);
        Assert.Equal("https://www.youtube.com/@relative", channels[2].Url);
    }

    [Fact]
    public void ParseChannels_ReturnsEmptyOnEmptyOrWhitespaceOutput()
    {
        Assert.Empty(YtDlpSubscriptionsParser.ParseChannels(string.Empty));
        Assert.Empty(YtDlpSubscriptionsParser.ParseChannels("   \n\t  "));
    }
}
