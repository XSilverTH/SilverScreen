using GLib;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SilverScreen.Account.Auth;
using SilverScreen.Account.Profile;
using SilverScreen.Browsing.Home;
using SilverScreen.Browsing.Search;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Core.Browsing.Search;
using SilverScreen.Core.Browsing.Subscriptions;
using SilverScreen.Core.Common;
using SilverScreen.Core.Domain;
using SilverScreen.Core.Downloads;
using SilverScreen.Core.Player;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Core.Preferences;
using SilverScreen.Core.Queue;
using SilverScreen.Infrastructure.Account.Profile;
using SilverScreen.Infrastructure.Account.Session;
using SilverScreen.Infrastructure.Browsing.Channel;
using SilverScreen.Infrastructure.Browsing.History;
using SilverScreen.Infrastructure.Browsing.Home;
using SilverScreen.Infrastructure.Browsing.Search;
using SilverScreen.Infrastructure.Browsing.Subscriptions;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.Domain;
using SilverScreen.Infrastructure.Downloads;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Infrastructure.Player.Comments;
using SilverScreen.Infrastructure.Preferences;
using SilverScreen.Infrastructure.Queue;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Shell;

/// <summary>Narrow facade over browsing services consumed by the application shell.</summary>
public sealed record BrowsingServices(
    ISearchService Search,
    ISearchSuggestionService SearchSuggestions,
    IChannelService Channels,
    IThumbnailService Thumbnails,
    HomeFeedCoordinator HomeFeed,
    IAuthenticatedHistoryService History,
    IAuthenticatedSubscriptionsService Subscriptions);

/// <summary>Narrow facade over account/session services consumed by the application shell.</summary>
public sealed record AccountServices(
    ISessionService Session,
    IAccountProfileService AccountProfile,
    IPreferencesService Preferences,
    IQueueService Queue,
    INativeAuthService Auth);

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
        services.AddSingleton<NavigationService>(static _ => new NavigationService());
        services.AddSingleton<INavigationService>(static provider =>
            provider.GetRequiredService<NavigationService>());
        services.AddSingleton<NotificationService>(static _ => new NotificationService());
        services.AddSingleton<INotificationService>(static provider =>
            provider.GetRequiredService<NotificationService>());
        services.AddSingleton<SecretServiceSessionService>(static provider =>
            new SecretServiceSessionService(provider.GetRequiredService<IAccountProfileService>));
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
        services.AddSingleton<IPlaybackPresenceService>(static provider =>
            new LazyDiscordPresenceService(
                provider.GetRequiredService<IPreferencesService>(),
                provider.GetRequiredService<ApplicationConfiguration>().DiscordApplicationId));
        services.AddSingleton<IYouTubePlaybackTelemetryService, YouTubePlaybackTelemetryService>();
        services.AddSingleton<PlaybackCoordinator>();
        services.AddSingleton<IYouTubePlaybackProgressService, YoutubeApiPlaybackProgressService>();
        services.AddSingleton<IPlaybackService, ExternalMpvPlaybackService>();
        services.AddSingleton(static provider =>
            new Lazy<IPlaybackService>(provider.GetRequiredService<IPlaybackService>));
        // yt-dlp runner and media resolver back the mpv fallback path: when mpv exits fast
        // with a non-zero code, the playback service resolves direct media URLs and relaunches once.
        services.AddSingleton<IYtDlpRunner, YtDlpRunner>();
        services.AddSingleton<ISearchService, YoutubeApiSearchService>();
        services.AddSingleton<ISearchSuggestionService, YoutubeApiSearchSuggestionService>();
        services.AddSingleton<IChannelService, YoutubeApiChannelService>();
        services.AddSingleton<IVideoEngagementService, ReturnYouTubeDislikeService>();
        services.AddSingleton<IYouTubeRatingService, YoutubeApiRatingService>();
        services.AddSingleton<ISponsorBlockService, SponsorBlockService>();
        services.AddSingleton<IThumbnailService, ThumbnailCacheService>();
        services.AddSingleton<ILibraryService, JsonLibraryService>();
        services.AddSingleton<DownloadCoordinatorService>();
        services.AddSingleton<IDownloadService>(static provider =>
            provider.GetRequiredService<DownloadCoordinatorService>());
        services.AddSingleton<YtDlpMediaResolver>();
        services.AddSingleton<IYouTubeMediaResolver>(static provider =>
            provider.GetRequiredService<YtDlpMediaResolver>());
        services.AddSingleton<IYouTubeCommentService, YoutubeApiCommentService>();
        services.AddSingleton<IAuthenticatedHomeFeedService, YoutubeApiHomeFeedService>();
        services.AddSingleton<IAuthenticatedHistoryService, YoutubeApiHistoryService>();
        services.AddSingleton<IAuthenticatedSubscriptionsService, YoutubeApiSubscriptionsService>();
        services.AddSingleton<HomeFeedCoordinator>();
        services.AddSingleton<RuntimeDependencyDiagnostics>();
        services.AddSingleton(static provider => new BrowsingServices(
            provider.GetRequiredService<ISearchService>(),
            provider.GetRequiredService<ISearchSuggestionService>(),
            provider.GetRequiredService<IChannelService>(),
            provider.GetRequiredService<IThumbnailService>(),
            provider.GetRequiredService<HomeFeedCoordinator>(),
            provider.GetRequiredService<IAuthenticatedHistoryService>(),
            provider.GetRequiredService<IAuthenticatedSubscriptionsService>()));
        services.AddSingleton<INativeAuthService, NativeAuthService>();
        services.AddSingleton(static provider => new AccountServices(
            provider.GetRequiredService<ISessionService>(),
            provider.GetRequiredService<IAccountProfileService>(),
            provider.GetRequiredService<IPreferencesService>(),
            provider.GetRequiredService<IQueueService>(),
            provider.GetRequiredService<INativeAuthService>()));
        services.AddSingleton<PlayerDependencies>();
        services.AddSingleton(static provider =>
            new Lazy<PlayerDependencies>(provider.GetRequiredService<PlayerDependencies>));
        services.AddSingleton<ShellRefreshCoordinator>();
        services.AddSingleton<IShellRefreshCoordinator>(static provider =>
            provider.GetRequiredService<ShellRefreshCoordinator>());
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton(static provider => new AccountViewModel(
            provider.GetRequiredService<IAccountProfileService>(),
            provider.GetRequiredService<ISessionService>(),
            DispatchToMainThread));
        return services;
    }

    private static void DispatchToMainThread(Action action)
    {
        if (MainContext.Default().IsOwner())
        {
            action();
            return;
        }

        Functions.IdleAdd(0, () =>
        {
            action();
            return false;
        });
    }

    private sealed class LazyDiscordPresenceService(
        IPreferencesService preferencesService,
        string? applicationId)
        : IPlaybackPresenceService
    {
        private readonly Lazy<DiscordPresenceService> _inner = new(() =>
            new DiscordPresenceService(preferencesService, applicationId));

        private int _disposed;

        public void SetPlaybackState(PlaybackRequest request, PlaybackPresenceState state)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            _inner.Value.SetPlaybackState(request, state);
        }

        public void Clear()
        {
            if (Volatile.Read(ref _disposed) != 0 || !_inner.IsValueCreated)
                return;

            _inner.Value.Clear();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            if (_inner.IsValueCreated)
                _inner.Value.Dispose();
        }
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
#if DEBUG
                ValidateOnBuild = true,
                ValidateScopes = true
#endif
            });
    }

    internal static void SweepStaleTemporaryFiles()
    {
        // Best-effort orphan cleanup for crashed/short-lived cookie and IPC entries; never fails startup.
        try
        {
            TemporaryCookieFile.SweepStale();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Stale temporary file sweep failed at startup");
        }
    }
}