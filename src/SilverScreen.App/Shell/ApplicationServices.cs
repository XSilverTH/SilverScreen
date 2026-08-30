using Microsoft.Extensions.DependencyInjection;
using SilverScreen.Browsing.Home;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Core.Browsing.Search;
using SilverScreen.Core.Browsing.Subscriptions;
using SilverScreen.Core.Player;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Core.Preferences;
using SilverScreen.Core.Queue;
using SilverScreen.Infrastructure.Account.Profile;
using SilverScreen.Infrastructure.Account.Session;
using SilverScreen.Infrastructure.Browsing.Channel;
using SilverScreen.Infrastructure.Browsing.Common;
using SilverScreen.Infrastructure.Browsing.History;
using SilverScreen.Infrastructure.Browsing.Home;
using SilverScreen.Infrastructure.Browsing.Search;
using SilverScreen.Infrastructure.Browsing.Subscriptions;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Infrastructure.Player.Comments;
using SilverScreen.Infrastructure.Preferences;
using SilverScreen.Infrastructure.Queue;
using SilverScreen.Infrastructure.YouTube;
using SilverScreen.Player;

namespace SilverScreen.Shell;

/// <summary>Provides the services consumed by the application shell.</summary>
public sealed class ApplicationServices(
    IPreferencesService preferences,
    IQueueService queue,
    ISessionService session,
    IAccountProfileService accountProfile,
    IPlaybackService playback,
    ISearchService search,
    ISearchSuggestionService searchSuggestions,
    IChannelService channels,
    IThumbnailService thumbnails,
    HomeFeedCoordinator homeFeed,
    IAuthenticatedHistoryService history,
    IAuthenticatedSubscriptionsService subscriptions,
    RuntimeDependencyDiagnostics runtimeDependencyDiagnostics,
    IWatchProgressService watchProgress,
    PlayerDependencies player)
{
    public IPreferencesService Preferences { get; } = preferences;
    public IQueueService Queue { get; } = queue;
    public ISessionService Session { get; } = session;
    public IAccountProfileService AccountProfile { get; } = accountProfile;
    public IPlaybackService Playback { get; } = playback;
    public ISearchService Search { get; } = search;
    public ISearchSuggestionService SearchSuggestions { get; } = searchSuggestions;
    public IChannelService Channels { get; } = channels;
    public IThumbnailService Thumbnails { get; } = thumbnails;
    public HomeFeedCoordinator HomeFeed { get; } = homeFeed;
    public IAuthenticatedHistoryService History { get; } = history;
    public IAuthenticatedSubscriptionsService Subscriptions { get; } = subscriptions;
    public RuntimeDependencyDiagnostics RuntimeDependencyDiagnostics { get; } = runtimeDependencyDiagnostics;
    public IWatchProgressService WatchProgress { get; } = watchProgress;
    public PlayerDependencies Player { get; } = player;
}

/// <summary>Registers the application's production services.</summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddSilverScreenServices(
        this IServiceCollection services,
        ApplicationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(configuration);
        services.AddSingleton<IPreferencesService, FilePreferencesService>();
        services.AddSingleton<IQueueService, QueueService>();
        services.AddSingleton<SecretServiceSessionService>(static provider =>
            new SecretServiceSessionService(provider.GetRequiredService<IAuthenticatedHomeFeedService>));
        services.AddSingleton<ISessionService>(static provider =>
            provider.GetRequiredService<SecretServiceSessionService>());
        services.AddSingleton<ISecretServiceAvailability>(static provider =>
            provider.GetRequiredService<SecretServiceSessionService>());
        services.AddSingleton<ICookieFileProvider>(static provider =>
            provider.GetRequiredService<SecretServiceSessionService>());
        services.AddSingleton<YouTubeClientProvider>();
        services.AddSingleton<IYouTubeClientProvider>(static provider =>
            provider.GetRequiredService<YouTubeClientProvider>());
        services.AddSingleton<IAccountProfileService, YoutubeApiAccountProfileService>();
        services.AddSingleton<MpvCommandBuilder>();
        services.AddSingleton<IPlaybackPresenceService>(provider =>
            new DiscordPresenceService(
                provider.GetRequiredService<IPreferencesService>(),
                provider.GetRequiredService<ApplicationConfiguration>().DiscordApplicationId));
        services.AddSingleton<IYouTubePlaybackTelemetryService, YouTubePlaybackTelemetryService>();
        services.AddSingleton<PlaybackCoordinator>();
        services.AddSingleton<IPlaybackService, ExternalMpvPlaybackService>();
        services.AddSingleton<IWatchProgressService, FileWatchProgressService>();
        // yt-dlp is retained only for raw media stream extraction used by MPV.
        services.AddSingleton<IYtDlpRunner, YtDlpRunner>();
        services.AddSingleton<ISearchService, YoutubeApiSearchService>();
        services.AddSingleton<ISearchSuggestionService, YoutubeApiSearchSuggestionService>();
        services.AddSingleton<IChannelService, YoutubeApiChannelService>();
        services.AddSingleton<IVideoEngagementService, ReturnYouTubeDislikeService>();
        services.AddSingleton<IYouTubeRatingService, YoutubeApiRatingService>();
        services.AddSingleton<ISponsorBlockService, SponsorBlockService>();
        services.AddSingleton<IThumbnailService, ThumbnailCacheService>();
        services.AddSingleton<YtDlpMediaResolver>();
        services.AddSingleton<IYouTubeMediaResolver>(static provider =>
            provider.GetRequiredService<YtDlpMediaResolver>());
        services.AddSingleton<IYouTubeCommentService, YoutubeApiCommentService>();
        services.AddSingleton<IAuthenticatedHomeFeedService, YoutubeApiHomeFeedService>();
        services.AddSingleton<IAuthenticatedHistoryService, YoutubeApiHistoryService>();
        services.AddSingleton<IAuthenticatedSubscriptionsService, YoutubeApiSubscriptionsService>();
        services.AddSingleton<HomeFeedCoordinator>();
        services.AddSingleton<RuntimeDependencyDiagnostics>();
        services.AddSingleton<PlayerDependencies>();
        services.AddSingleton<ApplicationServices>();
        return services;
    }
}

/// <summary>Builds the application's production service provider.</summary>
public static class ApplicationComposition
{
    public static ServiceProvider CreateServiceProvider(ApplicationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new ServiceCollection()
            .AddSilverScreenServices(configuration)
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
    }
}