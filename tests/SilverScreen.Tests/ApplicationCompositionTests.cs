using Microsoft.Extensions.DependencyInjection;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Session;

namespace SilverScreen.Tests;

public sealed class ApplicationCompositionTests
{
    [Fact]
    public void RegistrationsAllowServicesAndConfigurationToBeSubstituted()
    {
        var configuration = new ApplicationConfiguration
        {
            DiscordApplicationId = "test-discord-application-id"
        };
        var preferences = new InMemoryPreferencesService();
        var session = new InMemorySessionService();
        var playback = new FakePlaybackService();
        var secretServiceAvailability = new SecretServiceAvailability();
        var collection = new ServiceCollection();

        collection.AddSilverScreenServices(configuration);
        collection.AddSingleton<IPreferencesService>(preferences);
        collection.AddSingleton<ISessionService>(session);
        collection.AddSingleton<ISecretServiceAvailability>(secretServiceAvailability);
        collection.AddSingleton<IPlaybackService>(playback);

        using var provider = collection.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        var services = provider.GetRequiredService<ApplicationServices>();

        Assert.Same(configuration, provider.GetRequiredService<ApplicationConfiguration>());
        Assert.Same(preferences, services.Preferences);
        Assert.Same(session, services.Session);
        Assert.Same(playback, services.Playback);
        Assert.NotNull(services.RuntimeDependencyDiagnostics);
    }

    private sealed class InMemoryPreferencesService : IPreferencesService
    {
        private AppPreferences _preferences = new();

        public event EventHandler<AppPreferences>? PreferencesChanged;

        public AppPreferences GetPreferences() => _preferences;

        public void SavePreferences(AppPreferences preferences)
        {
            _preferences = preferences;
            PreferencesChanged?.Invoke(this, preferences);
        }
    }

    private sealed class FakePlaybackService : IPlaybackService
    {
        public Task<string> PlayAsync(PlaybackRequest request) => Task.FromResult("Played.");
    }

    private sealed class SecretServiceAvailability : ISecretServiceAvailability
    {
        public bool IsAvailable => true;
    }
}
