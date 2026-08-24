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

namespace SilverScreen.Core.Queue;

public interface IQueueService
{
    IReadOnlyList<QueueItem> Items { get; }

    TimeSpan TotalDuration { get; }
    event EventHandler? Changed;

    QueueItem Add(VideoSummary video);

    void Move(Guid itemId, int destinationIndex);

    void Remove(Guid itemId);

    void Clear();

    void Replace(IEnumerable<VideoSummary> videos);
}