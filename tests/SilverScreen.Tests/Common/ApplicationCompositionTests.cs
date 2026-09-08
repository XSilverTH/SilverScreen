using Microsoft.Extensions.DependencyInjection;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Player;
using SilverScreen.Infrastructure.Account.Profile;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Player;
using SilverScreen.Shell;

namespace SilverScreen.Tests.Common;

public sealed class ApplicationCompositionTests
{
    [Fact]
    public void CreateServiceProvider_ValidatesOnBuildAndResolvesFacades()
    {
        var configuration = new ApplicationConfiguration
        {
            DiscordApplicationId = "test-discord-app-id"
        };

        using var provider = ApplicationComposition.CreateServiceProvider(configuration);

        var browsing = provider.GetRequiredService<BrowsingServices>();
        Assert.NotNull(browsing.Search);
        Assert.NotNull(browsing.SearchSuggestions);
        Assert.NotNull(browsing.Channels);
        Assert.NotNull(browsing.Thumbnails);
        Assert.NotNull(browsing.HomeFeed);
        Assert.NotNull(browsing.History);
        Assert.NotNull(browsing.Subscriptions);

        var account = provider.GetRequiredService<AccountServices>();
        Assert.NotNull(account.Preferences);
        Assert.NotNull(account.Queue);
        Assert.NotNull(account.Session);
        Assert.NotNull(account.AccountProfile);

        Assert.NotNull(provider.GetRequiredService<IPlaybackService>());
        Assert.NotNull(provider.GetRequiredService<RuntimeDependencyDiagnostics>());

        var player = provider.GetRequiredService<PlayerDependencies>();
        Assert.NotNull(player.Preferences);
        Assert.NotNull(player.CookieFiles);
        Assert.NotNull(player.PlaybackPresence);
        Assert.NotNull(player.PlaybackTelemetry);
        Assert.NotNull(player.PlaybackProgress);
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