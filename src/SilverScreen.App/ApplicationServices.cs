using Microsoft.Extensions.DependencyInjection;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Diagnostics;
using SilverScreen.Infrastructure.Features.Engagement;
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
    IAccountProfileService accountProfile,
    IPlaybackService playback,
    ISearchService search,
    IThumbnailService thumbnails,
    HomeFeedCoordinator homeFeed,
    SessionValidationCoordinator sessionValidation,
    RuntimeDependencyDiagnostics runtimeDependencyDiagnostics,
    ICookieFileProvider cookieFiles,
    IPlaybackPresenceService playbackPresence,
    IYouTubePlaybackTelemetryService playbackTelemetry,
    IVideoEngagementService videoEngagement,
    IYouTubeRatingService youtubeRating,
    IYouTubeCommentService comments)
{
    public IPreferencesService Preferences { get; } = preferences;
    public IQueueService Queue { get; } = queue;
    public ISessionService Session { get; } = session;
    public IAccountProfileService AccountProfile { get; } = accountProfile;
    public IPlaybackService Playback { get; } = playback;
    public ISearchService Search { get; } = search;
    public IThumbnailService Thumbnails { get; } = thumbnails;
    public HomeFeedCoordinator HomeFeed { get; } = homeFeed;
    public RuntimeDependencyDiagnostics RuntimeDependencyDiagnostics { get; } = runtimeDependencyDiagnostics;
    public SessionValidationCoordinator SessionValidation { get; } = sessionValidation;
    public ICookieFileProvider CookieFiles { get; } = cookieFiles;
    public IPlaybackPresenceService PlaybackPresence { get; } = playbackPresence;
    public IYouTubePlaybackTelemetryService PlaybackTelemetry { get; } = playbackTelemetry;
    public IVideoEngagementService VideoEngagement { get; } = videoEngagement;
    public IYouTubeRatingService YouTubeRating { get; } = youtubeRating;
    public IYouTubeCommentService Comments { get; } = comments;
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
        services.AddSingleton<IAccountProfileService>(provider =>
            new YouTubeAccountProfileService(
                new HttpClient(),
                provider.GetRequiredService<ISessionService>()));
        services.AddSingleton<MpvCommandBuilder>();
        services.AddSingleton<IPlaybackPresenceService>(provider =>
            new DiscordPresenceService(
                provider.GetRequiredService<IPreferencesService>(),
                provider.GetRequiredService<ApplicationConfiguration>().DiscordApplicationId));
        services.AddSingleton<IYouTubePlaybackTelemetryService, YouTubePlaybackTelemetryService>();
        services.AddSingleton<IPlaybackService, ExternalMpvPlaybackService>();
        services.AddSingleton<YtDlpRunner>();
        services.AddSingleton<IYtDlpRunner>(static provider => provider.GetRequiredService<YtDlpRunner>());
        services.AddSingleton<IYtDlpProcessRunner>(static provider => provider.GetRequiredService<YtDlpRunner>());
        services.AddSingleton<ISearchService, YtDlpSearchService>();
        services.AddSingleton<IVideoEngagementService, ReturnYouTubeDislikeService>();
        services.AddSingleton<IYouTubeRatingService, YouTubeRatingService>();
        services.AddSingleton<IThumbnailService, ThumbnailCacheService>();
        services.AddSingleton<IYouTubeHomeClient>(provider =>
            new YtDlpHomeClient(
                provider.GetRequiredService<ISessionService>(),
                provider.GetRequiredService<ICookieFileProvider>(),
                provider.GetRequiredService<IPreferencesService>().GetPreferences().YtDlpExecutablePath,
                processRunner: provider.GetRequiredService<IYtDlpProcessRunner>()));
        services.AddSingleton<IYouTubeCommentService>(provider =>
            new YtDlpCommentService(
                provider.GetRequiredService<ICookieFileProvider>(),
                provider.GetRequiredService<IPreferencesService>().GetPreferences().YtDlpExecutablePath,
                processRunner: provider.GetRequiredService<IYtDlpProcessRunner>()));
        services.AddSingleton<IAuthenticatedHomeFeedService, AuthenticatedHomeFeedService>();
        services.AddSingleton<HomeFeedCoordinator>();
        services.AddSingleton<HomeSessionValidator>();
        services.AddSingleton<RuntimeDependencyDiagnostics>();
        services.AddSingleton<SessionValidationCoordinator>();
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