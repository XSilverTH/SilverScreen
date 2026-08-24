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

namespace SilverScreen.Player;

/// <summary>Services and dependencies required for embedded playback and player sub-controllers.</summary>
public sealed record PlayerDependencies(
    IPreferencesService Preferences,
    ICookieFileProvider CookieFiles,
    IPlaybackPresenceService PlaybackPresence,
    IYouTubePlaybackTelemetryService PlaybackTelemetry,
    IWatchProgressService WatchProgress,
    IVideoEngagementService VideoEngagement,
    IYouTubeRatingService YouTubeRating,
    ISponsorBlockService SponsorBlock,
    ISessionService Session,
    IYouTubeCommentService Comments,
    IYouTubeVideoDetailsService VideoDetails,
    IQueueService Queue,
    IThumbnailService Thumbnails);