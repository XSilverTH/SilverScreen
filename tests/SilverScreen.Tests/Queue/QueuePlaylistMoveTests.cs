using SilverScreen.Core.Browsing.Common;
using SilverScreen.Queue;

namespace SilverScreen.Tests.Queue;

public sealed class QueuePlaylistMoveTests
{
    [Fact]
    public void TranslatesMovingLastItemToFront()
    {
        var (from, to) = Translate(["a", "b", "c"], ["c", "a", "b"]);

        Assert.Equal(2, from);
        Assert.Equal(0, to);
    }

    [Fact]
    public void TranslatesMovingFirstItemToBack()
    {
        var (from, to) = Translate(["a", "b", "c"], ["b", "c", "a"]);

        Assert.Equal(0, from);
        Assert.Equal(2, to);
    }
    [Fact]
    public void TranslatesSwap()
    {
        var (from, to) = Translate(["a", "b", "c"], ["b", "a", "c"]);

        Assert.Equal(0, from);
        Assert.Equal(1, to);
    }

    [Fact]
    public void TranslatesAnArbitraryMiddleReorder()
    {
        var (from, to) = Translate(["a", "b", "c", "d", "e"], ["a", "d", "b", "c", "e"]);

        Assert.Equal(3, from);
        Assert.Equal(1, to);
    }

    private static (int From, int To) Translate(string[] current, string[] next)
    {
        var currentVideos = current.Select(Video).ToArray();
        var nextVideos = next.Select(Video).ToArray();

        Assert.True(QueuePlaylistMove.TryTranslate(currentVideos, nextVideos, out var from, out var to));
        return (from, to);
    }

    private static VideoSummary Video(string id) =>
        new(id, id, "channel", TimeSpan.Zero, string.Empty, false);
}
