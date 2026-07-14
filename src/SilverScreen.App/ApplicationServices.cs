using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Feed;
using SilverScreen.Infrastructure.Features.Playback;
using SilverScreen.Infrastructure.Features.Queue;
using SilverScreen.Infrastructure.Features.Search;
using SilverScreen.Infrastructure.Features.Session;
using SilverScreen.Infrastructure.Features.Thumbnails;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen;

/// <summary>Owns the single application-wide instance of each cross-feature service.</summary>
public sealed class ApplicationServices : IDisposable
{
    public ApplicationServices()
    {
        Queue = new QueueService();
        Session = new SecretServiceSessionService();
        CookieFiles = new TemporaryCookieFileProvider(Session);
        Playback = new ExternalMpvPlaybackService(new PlaybackOptions(), new MpvCommandBuilder(), CookieFiles);
        Search = new YtDlpSearchService(new YtDlpOptions(), new YtDlpRunner(CookieFiles));
        Thumbnails = new ThumbnailCacheService();
        YouTubeHomeClient = new YtDlpHomeClient(Session, CookieFiles);
        AuthenticatedHomeFeed = new AuthenticatedHomeFeedService(YouTubeHomeClient, Session);
        HomeFeed = new HomeFeedCoordinator(Session, AuthenticatedHomeFeed);
        HomeSessionValidator = new HomeSessionValidator(AuthenticatedHomeFeed);
        SessionValidation = new SessionValidationCoordinator(HomeSessionValidator, Session);
    }

    public IQueueService Queue { get; }
    public ISessionService Session { get; }
    public ICookieFileProvider CookieFiles { get; }
    public IPlaybackService Playback { get; }
    public ISearchService Search { get; }
    public IThumbnailService Thumbnails { get; }
    public IYouTubeHomeClient YouTubeHomeClient { get; }
    public AuthenticatedHomeFeedService AuthenticatedHomeFeed { get; }
    public HomeFeedCoordinator HomeFeed { get; }
    public HomeSessionValidator HomeSessionValidator { get; }
    public SessionValidationCoordinator SessionValidation { get; }

    public void Dispose()
    {
        SessionValidation.Dispose();
        HomeFeed.Dispose();
        AuthenticatedHomeFeed.Dispose();
        if (Thumbnails is IDisposable disposableThumbnails)
        {
            disposableThumbnails.Dispose();
        }
    }
}