using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Queue;
using SilverScreen.Queue;

namespace SilverScreen.Tests.Queue;

public sealed class QueueItemRowViewTests
{
    [Fact]
    public void RecycledRowActionsUseTheCurrentlyBoundItemAndPosition()
    {
        var first = CreateItem(Guid.NewGuid(), "first");
        var second = CreateItem(Guid.NewGuid(), "second");
        var binding = new QueueRowBinding();
        binding.Bind(first, 0);
        binding.Bind(second, 3);

        Assert.Same(second, binding.Item);
        Assert.True(binding.TryGetPlayTarget(out var playId, out var playIndex));
        Assert.Equal(second.Id, playId);
        Assert.Equal(3, playIndex);
        Assert.True(binding.TryGetMoveTarget(-1, out var moveUpId, out var moveUpIndex));
        Assert.Equal(second.Id, moveUpId);
        Assert.Equal(2, moveUpIndex);
        Assert.True(binding.TryGetMoveTarget(1, out var moveDownId, out var moveDownIndex));
        Assert.Equal(second.Id, moveDownId);
        Assert.Equal(4, moveDownIndex);
        Assert.Equal(4, binding.ResolveDropIndex(false));
        Assert.Equal(3, binding.ResolveDropIndex(true));
    }

    private static QueueItem CreateItem(Guid id, string title)
    {
        var video = new VideoSummary(
            id.ToString(), title, "channel", TimeSpan.FromMinutes(1), "thumbnail", false);
        return new QueueItem(id, video, DateTimeOffset.UtcNow);
    }
}
