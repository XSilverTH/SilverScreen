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


namespace SilverScreen.Tests.Common;

public sealed class RuntimeDependencyDiagnosticsTests
{
    [Fact]
    public void ExternalBackendChecksOnlyTheMpvExecutable()
    {
        var preferences = new TestPreferences(PlaybackBackends.ExternalMpv);
        var diagnostics = new RuntimeDependencyDiagnostics(preferences, new TestSecretService(true),
            path => path != "mpv",
            () => false);

        var warnings = diagnostics.GetStartupWarnings();

        Assert.Contains(warnings, warning => warning.Contains("MPV could not be started", StringComparison.Ordinal));
        Assert.DoesNotContain(RuntimeDependencyGuidance.LibMpvUnavailable, warnings);
    }

    [Fact]
    public void EmbeddedBackendChecksOnlyLibMpvAndKeepsOtherWarnings()
    {
        var preferences = new TestPreferences(PlaybackBackends.EmbeddedPlayer);
        var diagnostics =
            new RuntimeDependencyDiagnostics(preferences, new TestSecretService(false), _ => false, () => false);

        var warnings = diagnostics.GetStartupWarnings();

        Assert.Contains(RuntimeDependencyGuidance.LibMpvUnavailable, warnings);
        Assert.Contains(RuntimeDependencyGuidance.SecretServiceUnavailable, warnings);
        Assert.Contains(warnings, warning => warning.Contains("yt-dlp could not be started", StringComparison.Ordinal));
        Assert.DoesNotContain(warnings,
            warning => warning.Contains("MPV could not be started", StringComparison.Ordinal));
    }

    private sealed class TestPreferences(string playbackBackend) : IPreferencesService
    {
        private readonly AppPreferences _preferences = new()
        {
            PlaybackBackend = playbackBackend,
            MpvExecutablePath = "mpv",
            YtDlpExecutablePath = "yt-dlp"
        };

        public event EventHandler<AppPreferences>? PreferencesChanged;

        public AppPreferences GetPreferences()
        {
            return _preferences;
        }

        public void SavePreferences(AppPreferences preferences)
        {
            PreferencesChanged?.Invoke(this, preferences);
        }
    }

    private sealed class TestSecretService(bool isAvailable) : ISecretServiceAvailability
    {
        public bool IsAvailable { get; } = isAvailable;
    }
}
