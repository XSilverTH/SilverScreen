using SilverScreen.Core.Services;
using SilverScreen.ViewModels;
namespace SilverScreen.Views.Player;

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
