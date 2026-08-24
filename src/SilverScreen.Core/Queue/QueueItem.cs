using SilverScreen.Core.Common;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Search;
using SilverScreen.Core.Browsing.History;
namespace SilverScreen.Core.Queue;

public sealed record QueueItem(Guid Id, VideoSummary Video, DateTimeOffset AddedAt);