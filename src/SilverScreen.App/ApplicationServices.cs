using Microsoft.Extensions.DependencyInjection;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Feed;
using SilverScreen.Infrastructure.Features.Playback;
using SilverScreen.Infrastructure.Features.Preferences;
using SilverScreen.Infrastructure.Features.Queue;
using SilverScreen.Infrastructure.Features.Search;
using SilverScreen.Infrastructure.Features.Session;
using SilverScreen.Infrastructure.Features.Thumbnails;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen;

/// <summary>Provides the services consumed by the application shell.</summary>
public sealed class ApplicationServices(
    IPreferencesService preferences,
    IQueueService queue,
    ISessionService session,
    IPlaybackService playback,
    ISearchService search,
    IThumbnailService thumbnails,
    HomeFeedCoordinator homeFeed,
    SessionValidationCoordinator sessionValidation)
{
    public IPreferencesService Preferences { get; } = preferences;
    public IQueueService Queue { get; } = queue;
    public ISessionService Session { get; } = session;
    public IPlaybackService Playback { get; } = playback;
    public ISearchService Search { get; } = search;
    public IThumbnailService Thumbnails { get; } = thumbnails;
    public HomeFeedCoordinator HomeFeed { get; } = homeFeed;
    public SessionValidationCoordinator SessionValidation { get; } = sessionValidation;
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
        services.AddSingleton<IPreferencesService>(static _ => new FilePreferencesService());
        services.AddSingleton<IQueueService>(static _ => new QueueService());
        services.AddSingleton<ISessionService>(static _ => new SecretServiceSessionService());
        services.AddSingleton<ICookieFileProvider>(static provider =>
            new TemporaryCookieFileProvider(provider.GetRequiredService<ISessionService>()));
        services.AddSingleton<MpvCommandBuilder>();
        services.AddSingleton<IPlaybackPresenceService>(provider =>
            new DiscordPresenceService(
                provider.GetRequiredService<IPreferencesService>(),
                provider.GetRequiredService<ApplicationConfiguration>().DiscordApplicationId));
        services.AddSingleton<IPlaybackService>(provider =>
            new ExternalMpvPlaybackService(
                provider.GetRequiredService<IPreferencesService>(),
                provider.GetRequiredService<MpvCommandBuilder>(),
                provider.GetRequiredService<ICookieFileProvider>(),
                provider.GetRequiredService<IPlaybackPresenceService>()));
        services.AddSingleton<YtDlpRunner>(static provider =>
            new YtDlpRunner(provider.GetRequiredService<ICookieFileProvider>()));
        services.AddSingleton<IYtDlpRunner>(static provider => provider.GetRequiredService<YtDlpRunner>());
        services.AddSingleton<IYtDlpProcessRunner>(static provider => provider.GetRequiredService<YtDlpRunner>());
        services.AddSingleton<ISearchService>(provider =>
            new YtDlpSearchService(
                provider.GetRequiredService<IPreferencesService>(),
                provider.GetRequiredService<IYtDlpRunner>()));
        services.AddSingleton<IThumbnailService>(static _ => new ThumbnailCacheService());
        services.AddSingleton<IYouTubeHomeClient>(provider =>
            new YtDlpHomeClient(
                provider.GetRequiredService<ISessionService>(),
                provider.GetRequiredService<ICookieFileProvider>(),
                processRunner: provider.GetRequiredService<IYtDlpProcessRunner>()));
        services.AddSingleton<IAuthenticatedHomeFeedService>(provider =>
            new AuthenticatedHomeFeedService(
                provider.GetRequiredService<IYouTubeHomeClient>(),
                provider.GetRequiredService<ISessionService>()));
        services.AddSingleton<HomeFeedCoordinator>(provider =>
            new HomeFeedCoordinator(
                provider.GetRequiredService<ISessionService>(),
                provider.GetRequiredService<IAuthenticatedHomeFeedService>()));
        services.AddSingleton<HomeSessionValidator>(provider =>
            new HomeSessionValidator(provider.GetRequiredService<IAuthenticatedHomeFeedService>()));
        services.AddSingleton<SessionValidationCoordinator>(provider =>
            new SessionValidationCoordinator(
                provider.GetRequiredService<HomeSessionValidator>(),
                provider.GetRequiredService<ISessionService>()));
        services.AddSingleton<ApplicationServices>(provider =>
            new ApplicationServices(
                provider.GetRequiredService<IPreferencesService>(),
                provider.GetRequiredService<IQueueService>(),
                provider.GetRequiredService<ISessionService>(),
                provider.GetRequiredService<IPlaybackService>(),
                provider.GetRequiredService<ISearchService>(),
                provider.GetRequiredService<IThumbnailService>(),
                provider.GetRequiredService<HomeFeedCoordinator>(),
                provider.GetRequiredService<SessionValidationCoordinator>()));

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