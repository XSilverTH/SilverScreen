using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Diagnostics;

namespace SilverScreen.Tests;

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