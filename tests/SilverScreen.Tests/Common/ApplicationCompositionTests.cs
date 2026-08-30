using Microsoft.Extensions.DependencyInjection;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Player;
using SilverScreen.Infrastructure.Account.Profile;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Player;
using SilverScreen.Shell;

namespace SilverScreen.Tests.Common;

public sealed class ApplicationCompositionTests
{
    [Fact]
    public void CreateServiceProvider_ValidatesOnBuildAndResolvesApplicationServices()
    {
        var configuration = new ApplicationConfiguration
        {
            DiscordApplicationId = "test-discord-app-id"
        };

        using var provider = ApplicationComposition.CreateServiceProvider(configuration);

        var services = provider.GetRequiredService<ApplicationServices>();
        Assert.NotNull(services);
        Assert.NotNull(services.Preferences);
        Assert.NotNull(services.Queue);
        Assert.NotNull(services.Session);
        Assert.NotNull(services.AccountProfile);
        Assert.NotNull(services.Playback);
        Assert.NotNull(services.Search);
        Assert.NotNull(services.SearchSuggestions);
        Assert.NotNull(services.Channels);
        Assert.NotNull(services.Thumbnails);
        Assert.NotNull(services.HomeFeed);
        Assert.NotNull(services.History);
        Assert.NotNull(services.Subscriptions);
        Assert.NotNull(services.RuntimeDependencyDiagnostics);
        Assert.NotNull(services.WatchProgress);

        var player = services.Player;
        Assert.NotNull(player);
        Assert.NotNull(player.Preferences);
        Assert.NotNull(player.CookieFiles);
        Assert.NotNull(player.PlaybackPresence);
        Assert.NotNull(player.PlaybackTelemetry);
        Assert.NotNull(player.WatchProgress);
        Assert.NotNull(player.VideoEngagement);
        Assert.NotNull(player.YouTubeRating);
        Assert.NotNull(player.SponsorBlock);
        Assert.NotNull(player.Session);
        Assert.NotNull(player.Comments);
        Assert.NotNull(player.MediaResolver);
    }

    [Fact]
    public void KeyedServices_AreInjectedViaConstructorAttributes()
    {
        var configuration = new ApplicationConfiguration
        {
            DiscordApplicationId = "test-discord-app-id"
        };

        using var provider = ApplicationComposition.CreateServiceProvider(configuration);

        var accountProfile = provider.GetRequiredService<IAccountProfileService>();
        Assert.IsType<YoutubeApiAccountProfileService>(accountProfile);

        var ratingService = provider.GetRequiredService<IYouTubeRatingService>();
        Assert.IsType<YoutubeApiRatingService>(ratingService);

        var playerDeps = provider.GetRequiredService<PlayerDependencies>();
        Assert.NotNull(playerDeps);
    }
}