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
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.YouTube;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Infrastructure.Player.Comments;
using SilverScreen.Infrastructure.Browsing.Common;
using SilverScreen.Infrastructure.Browsing.Home;
using SilverScreen.Infrastructure.Browsing.Channel;
using SilverScreen.Infrastructure.Browsing.Search;
using SilverScreen.Infrastructure.Browsing.History;
using SilverScreen.Infrastructure.Queue;
using SilverScreen.Infrastructure.Account.Session;
using SilverScreen.Infrastructure.Account.Auth;
using SilverScreen.Infrastructure.Account.Profile;
using SilverScreen.Infrastructure.Preferences;
using SilverScreen.Shell;
using SilverScreen.Browsing.Components;
using SilverScreen.Browsing.Home;
using SilverScreen.Browsing.Channel;
using SilverScreen.Browsing.Search;
using SilverScreen.Browsing.History;
using SilverScreen.Player;
using SilverScreen.Player.Views;
using SilverScreen.Player.Controllers;
using SilverScreen.Player.Comments;
using SilverScreen.Queue;
using SilverScreen.Account.Profile;
using SilverScreen.Account.Auth;
using SilverScreen.Account.Session;
using SilverScreen.Preferences;

using Microsoft.Extensions.DependencyInjection;

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
