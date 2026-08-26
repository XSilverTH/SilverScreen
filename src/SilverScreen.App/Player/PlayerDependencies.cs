using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Core.Preferences;
using SilverScreen.Core.Queue;

namespace SilverScreen.Player;

/// <summary>Services and dependencies required for embedded playback and player sub-controllers.</summary>
public sealed record PlayerDependencies(
    IPreferencesService Preferences,
    PlaybackCoordinator PlaybackCoordinator,
    ICookieFileProvider CookieFiles,
    IPlaybackPresenceService PlaybackPresence,
    IYouTubePlaybackTelemetryService PlaybackTelemetry,
    IWatchProgressService WatchProgress,
    IVideoEngagementService VideoEngagement,
    IYouTubeRatingService YouTubeRating,
    ISponsorBlockService SponsorBlock,
    ISessionService Session,
    IYouTubeCommentService Comments,
    IYouTubeMediaResolver MediaResolver,
    IQueueService Queue,
    IThumbnailService Thumbnails);