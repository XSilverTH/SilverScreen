using SilverScreen.Infrastructure.Player.Comments;
using Microsoft.Extensions.DependencyInjection;
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
using SilverScreen.Browsing.Components;
using SilverScreen.Browsing.Home;
using SilverScreen.Browsing.Channel;
using SilverScreen.Browsing.Search;
using SilverScreen.Browsing.History;
using SilverScreen.Account.Profile;
using SilverScreen.Account.Auth;
using SilverScreen.Account.Session;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Infrastructure.Browsing.Common;
using SilverScreen.Infrastructure.Browsing.Home;
using SilverScreen.Infrastructure.Browsing.Channel;
using SilverScreen.Infrastructure.Browsing.Search;
using SilverScreen.Infrastructure.Browsing.History;
using SilverScreen.Infrastructure.Preferences;
using SilverScreen.Infrastructure.Queue;
using SilverScreen.Infrastructure.YouTube;
using SilverScreen.Infrastructure.Account.Session;
using SilverScreen.Infrastructure.Account.Auth;
using SilverScreen.Infrastructure.Account.Profile;
using SilverScreen.Player.Views;
using SilverScreen.Player.Controllers;
using SilverScreen.Player;
using SilverScreen.Player.Comments;

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
    SessionValidationCoordinator sessionValidation,
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
    public RuntimeDependencyDiagnostics RuntimeDependencyDiagnostics { get; } = runtimeDependencyDiagnostics;
    public SessionValidationCoordinator SessionValidation { get; } = sessionValidation;
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
        services.AddSingleton<SecretServiceSessionService>();
        services.AddSingleton<ISessionService>(static provider =>
            provider.GetRequiredService<SecretServiceSessionService>());
        services.AddSingleton<ISecretServiceAvailability>(static provider =>
            provider.GetRequiredService<SecretServiceSessionService>());
        services.AddSingleton<ICookieFileProvider, TemporaryCookieFileProvider>();
        services.AddKeyedSingleton<HttpClient>("youtube-account", static (_, _) => new HttpClient());
        services.AddKeyedSingleton<HttpClient>("youtube-rating", static (_, _) => new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        });
        services.AddSingleton<YouTubeAuthenticationService>();
        services.AddSingleton<IAccountProfileService, YouTubeAccountProfileService>();
        services.AddSingleton<MpvCommandBuilder>();
        services.AddSingleton<IPlaybackPresenceService>(provider =>
            new DiscordPresenceService(
                provider.GetRequiredService<IPreferencesService>(),
                provider.GetRequiredService<ApplicationConfiguration>().DiscordApplicationId));
        services.AddSingleton<IYouTubePlaybackTelemetryService, YouTubePlaybackTelemetryService>();
        services.AddSingleton<PlaybackCoordinator>();
        services.AddSingleton<IPlaybackService, ExternalMpvPlaybackService>();
        services.AddSingleton<IWatchProgressService, FileWatchProgressService>();
        services.AddSingleton<YtDlpRunner>();
        services.AddSingleton<IYtDlpRunner>(static provider => provider.GetRequiredService<YtDlpRunner>());
        services.AddSingleton<ISearchService, YtDlpSearchService>();
        services.AddSingleton<ISearchSuggestionService, YouTubeSearchSuggestionService>();
        services.AddSingleton<IChannelService, YtDlpChannelService>();
        services.AddSingleton<IVideoEngagementService, ReturnYouTubeDislikeService>();
        services.AddSingleton<IYouTubeRatingService, YouTubeRatingService>();
        services.AddSingleton<ISponsorBlockService, SponsorBlockService>();
        services.AddSingleton<IThumbnailService, ThumbnailCacheService>();
        services.AddSingleton<IYouTubeCommentService, YtDlpCommentService>();
        services.AddSingleton<IYouTubeVideoDetailsService, YtDlpVideoDetailsService>();
        services.AddSingleton<IAuthenticatedHomeFeedService, AuthenticatedHomeFeedService>();
        services.AddSingleton<IAuthenticatedHistoryService, AuthenticatedHistoryService>();
        services.AddSingleton<HomeFeedCoordinator>();
        services.AddSingleton<RuntimeDependencyDiagnostics>();
        services.AddSingleton<SessionValidationCoordinator>();
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