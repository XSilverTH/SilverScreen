using Microsoft.Extensions.DependencyInjection;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Engagement;
using SilverScreen.Infrastructure.YouTube;
using SilverScreen.Views.Player;

namespace SilverScreen.Tests;

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
        Assert.NotNull(services.RuntimeDependencyDiagnostics);
        Assert.NotNull(services.SessionValidation);
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
        Assert.NotNull(player.VideoDetails);
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
        Assert.IsType<YouTubeAccountProfileService>(accountProfile);

        var ratingService = provider.GetRequiredService<IYouTubeRatingService>();
        Assert.IsType<YouTubeRatingService>(ratingService);

        var playerDeps = provider.GetRequiredService<PlayerDependencies>();
        Assert.NotNull(playerDeps);
    }
}