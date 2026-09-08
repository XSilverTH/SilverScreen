using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Core.Preferences;
using SilverScreen.Core.Queue;

namespace SilverScreen.Player;

/// <summary>
///     Services and dependencies required for embedded playback and player sub-controllers.
///     <para>
///         Maintains the architectural split between the playback coordinator and player sessions:
///         <list type="bullet">
///             <item>
///                 <term>PlaybackCoordinator (Process Registry &amp; Multi-Request Lifetime)</term>
///                 <description>
///                     Coordinates playback session tracking across multiple player windows/views and concurrent
///                     requests, including backend routing, process lifetimes, and telemetry/presence for each active
///                     playback ID.
///                 </description>
///             </item>
///             <item>
///                 <term>PlaybackSession (Single-View Lifecycle)</term>
///                 <description>
///                     Owns a single view lifecycle (one video/view presentation: OSD, resume/restart prompts,
///                     SponsorBlock skip prompts, engagement, and timeline state). Sessions must not bypass the
///                     coordinator or spawn/manage player processes directly; all process lifetimes and backend
///                     routing must go through the coordinator.
///                 </description>
///             </item>
///         </list>
///     </para>
/// </summary>
public sealed record PlayerDependencies(
    IPreferencesService Preferences,
    PlaybackCoordinator PlaybackCoordinator,
    ICookieFileProvider CookieFiles,
    IPlaybackPresenceService PlaybackPresence,
    IYouTubePlaybackTelemetryService PlaybackTelemetry,
    IYouTubePlaybackProgressService PlaybackProgress,
    IVideoEngagementService VideoEngagement,
    IYouTubeRatingService YouTubeRating,
    ISponsorBlockService SponsorBlock,
    ISessionService Session,
    IYouTubeCommentService Comments,
    IYouTubeMediaResolver MediaResolver,
    IQueueService Queue,
    IThumbnailService Thumbnails);