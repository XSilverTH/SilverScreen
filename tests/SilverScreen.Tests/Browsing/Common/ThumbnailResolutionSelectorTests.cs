using SilverScreen.Infrastructure.YouTube;
using YoutubeAPI.Models.Common;

namespace SilverScreen.Tests.Browsing.Common;

public sealed class ThumbnailResolutionSelectorTests
{
    [Fact]
    public void SelectCardThumbnail_PrefersResolutionMatchingCardSize_OverMassive1080p()
    {
        var thumbnails = new List<Thumbnail>
        {
            new(new Uri("https://i.ytimg.com/vi/test/default.jpg"), 120, 90),
            new(new Uri("https://i.ytimg.com/vi/test/mqdefault.jpg"), 320, 180),
            new(new Uri("https://i.ytimg.com/vi/test/hqdefault.jpg"), 480, 360),
            new(new Uri("https://i.ytimg.com/vi/test/sddefault.jpg"), 640, 480),
            new(new Uri("https://i.ytimg.com/vi/test/maxresdefault.jpg"), 1920, 1080)
        };

        var selected = ThumbnailResolutionSelector.SelectCardThumbnail(thumbnails);

        // It should pick a matching thumbnail within [320..640], choosing the smallest fitting variant (320 or 480) rather than 1080p
        Assert.Equal("https://i.ytimg.com/vi/test/mqdefault.jpg", selected);
    }

    [Fact]
    public void SelectCardThumbnail_FallsBackToLargestAvailable_WhenAllBelowMinimum()
    {
        var thumbnails = new List<Thumbnail>
        {
            new(new Uri("https://i.ytimg.com/vi/test/tiny.jpg"), 80, 60),
            new(new Uri("https://i.ytimg.com/vi/test/small.jpg"), 160, 120)
        };

        var selected = ThumbnailResolutionSelector.SelectCardThumbnail(thumbnails);

        Assert.Equal("https://i.ytimg.com/vi/test/small.jpg", selected);
    }

    [Fact]
    public void SelectAvatarThumbnail_PrefersSmallResolution_OverHighRes()
    {
        var thumbnails = new List<Thumbnail>
        {
            new(new Uri("https://yt3.ggpht.com/avatar_small.jpg"), 88, 88),
            new(new Uri("https://yt3.ggpht.com/avatar_medium.jpg"), 176, 176),
            new(new Uri("https://yt3.ggpht.com/avatar_huge.jpg"), 900, 900)
        };

        var selected = ThumbnailResolutionSelector.SelectAvatarThumbnail(thumbnails);

        Assert.Equal("https://yt3.ggpht.com/avatar_small.jpg", selected);
    }
}
