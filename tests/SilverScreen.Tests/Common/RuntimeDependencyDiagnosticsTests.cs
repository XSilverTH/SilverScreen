using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Common;

namespace SilverScreen.Tests.Common;

public sealed class RuntimeDependencyDiagnosticsTests
{
    [Fact]
    public void ExternalBackendChecksOnlyTheMpvExecutable()
    {
        var preferences = new TestPreferences(PlaybackBackendKind.ExternalMpv);
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
        var preferences = new TestPreferences(PlaybackBackendKind.Embedded);
        var diagnostics =
            new RuntimeDependencyDiagnostics(preferences, new TestSecretService(false), _ => false, () => false);

        var warnings = diagnostics.GetStartupWarnings();

        Assert.Contains(RuntimeDependencyGuidance.LibMpvUnavailable, warnings);
        Assert.Contains(RuntimeDependencyGuidance.SecretServiceUnavailable, warnings);
        Assert.Contains(warnings, warning => warning.Contains("yt-dlp could not be started", StringComparison.Ordinal));
        Assert.DoesNotContain(warnings,
            warning => warning.Contains("MPV could not be started", StringComparison.Ordinal));
    }

    [Fact]
    public void UnconfiguredExecutablePathsFallBackToPathNames()
    {
        var preferences = new TestPreferences(PlaybackBackendKind.ExternalMpv)
        {
            YtDlpExecutablePath = string.Empty,
            MpvExecutablePath = "   "
        };
        var diagnostics = new RuntimeDependencyDiagnostics(preferences, new TestSecretService(true),
            path => path is "yt-dlp" or "mpv",
            () => true);

        Assert.Empty(diagnostics.GetStartupWarnings());
    }


    private sealed class TestPreferences(PlaybackBackendKind playbackBackend) : IPreferencesService
    {
        private readonly AppPreferences _preferences = new()
        {
            PlaybackBackendKind = playbackBackend,
            MpvExecutablePath = "mpv",
            YtDlpExecutablePath = "yt-dlp"
        };

        public string MpvExecutablePath
        {
            get => _preferences.MpvExecutablePath;
            set => _preferences.MpvExecutablePath = value;
        }

        public string YtDlpExecutablePath
        {
            get => _preferences.YtDlpExecutablePath;
            set => _preferences.YtDlpExecutablePath = value;
        }

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