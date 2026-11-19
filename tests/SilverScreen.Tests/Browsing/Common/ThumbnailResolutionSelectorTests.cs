using SilverScreen.Infrastructure.YouTube;
using YoutubeAPI.Models.Common;

namespace SilverScreen.Tests.Browsing.Common;

public sealed class ThumbnailResolutionSelectorTests
{
    [Fact]
    public void SelectCardThumbnail_PrefersCrisp480p_WhenFullRangeAvailable()
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

        // Prefers crisp 480p (hqdefault) for HiDPI displays over 320p and massive 1080p
        Assert.Equal("https://i.ytimg.com/vi/test/hqdefault.jpg", selected);
    }

    [Fact]
    public void SelectCardThumbnail_Prefers640p_WhenInPreferredRangeAnd480pNotAvailable()
    {
        var thumbnails = new List<Thumbnail>
        {
            new(new Uri("https://i.ytimg.com/vi/test/default.jpg"), 120, 90),
            new(new Uri("https://i.ytimg.com/vi/test/mqdefault.jpg"), 320, 180),
            new(new Uri("https://i.ytimg.com/vi/test/sddefault.jpg"), 640, 480),
            new(new Uri("https://i.ytimg.com/vi/test/maxresdefault.jpg"), 1920, 1080)
        };

        var selected = ThumbnailResolutionSelector.SelectCardThumbnail(thumbnails);

        Assert.Equal("https://i.ytimg.com/vi/test/sddefault.jpg", selected);
    }

    [Fact]
    public void SelectCardThumbnail_FallsBackTo320p_WhenNoPreferredVariantsAvailable()
    {
        var thumbnails = new List<Thumbnail>
        {
            new(new Uri("https://i.ytimg.com/vi/test/default.jpg"), 120, 90),
            new(new Uri("https://i.ytimg.com/vi/test/mqdefault.jpg"), 320, 180)
        };

        var selected = ThumbnailResolutionSelector.SelectCardThumbnail(thumbnails);

        Assert.Equal("https://i.ytimg.com/vi/test/mqdefault.jpg", selected);
    }

    [Fact]
    public void SelectCardThumbnail_FallsBackTo720p_WhenOnlyLargerVariantsAvailable()
    {
        var thumbnails = new List<Thumbnail>
        {
            new(new Uri("https://i.ytimg.com/vi/test/720p.jpg"), 1280, 720),
            new(new Uri("https://i.ytimg.com/vi/test/1080p.jpg"), 1920, 1080)
        };

        var selected = ThumbnailResolutionSelector.SelectCardThumbnail(thumbnails);

        Assert.Equal("https://i.ytimg.com/vi/test/720p.jpg", selected);
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
    public void SelectCardThumbnail_ReturnsEmpty_WhenNullOrEmpty()
    {
        Assert.Equal(string.Empty, ThumbnailResolutionSelector.SelectCardThumbnail(null!));
        Assert.Equal(string.Empty, ThumbnailResolutionSelector.SelectCardThumbnail(new List<Thumbnail>()));
    }

    [Theory]
    [InlineData(120, 90, "https://i.ytimg.com/vi/test/120.jpg")]
    [InlineData(320, 180, "https://i.ytimg.com/vi/test/320.jpg")]
    [InlineData(480, 360, "https://i.ytimg.com/vi/test/480.jpg")]
    [InlineData(640, 480, "https://i.ytimg.com/vi/test/640.jpg")]
    [InlineData(1920, 1080, "https://i.ytimg.com/vi/test/1080.jpg")]
    public void SelectCardThumbnail_HandlesSingleItem_RegardlessOfResolution(int width, int height, string url)
    {
        var thumbnails = new List<Thumbnail>
        {
            new(new Uri(url), width, height)
        };

        var selected = ThumbnailResolutionSelector.SelectCardThumbnail(thumbnails);

        Assert.Equal(url, selected);
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

    [Fact]
    public void SelectAvatarThumbnail_ReturnsNull_WhenNullOrEmpty()
    {
        Assert.Null(ThumbnailResolutionSelector.SelectAvatarThumbnail(null!));
        Assert.Null(ThumbnailResolutionSelector.SelectAvatarThumbnail(new List<Thumbnail>()));
    }
}
