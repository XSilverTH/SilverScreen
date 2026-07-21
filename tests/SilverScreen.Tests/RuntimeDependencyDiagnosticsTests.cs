using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Diagnostics;

namespace SilverScreen.Tests;

public sealed class RuntimeDependencyDiagnosticsTests
{
    [Fact]
    public void GetStartupWarnings_ReturnsNothingWhenAllDependenciesAreAvailable()
    {
        var diagnostics = CreateDiagnostics("yt-dlp", "mpv", secretServiceAvailable: true,
            executablePath => executablePath is "yt-dlp" or "mpv");

        var warnings = diagnostics.GetStartupWarnings();

        Assert.Empty(warnings);
    }

    [Fact]
    public void GetStartupWarnings_ExplainsHowToRecoverEachUnavailableDependency()
    {
        var diagnostics = CreateDiagnostics("/opt/tools/yt-dlp", "/opt/tools/mpv", secretServiceAvailable: false,
            _ => false);

        var warnings = diagnostics.GetStartupWarnings();

        Assert.Equal(
            [
                "yt-dlp could not be started from '/opt/tools/yt-dlp'. Install yt-dlp, then set its executable path in Preferences → Search (yt-dlp).",
                "MPV could not be started from '/opt/tools/mpv'. Install MPV, then set its executable path in Preferences → MPV Configuration.",
                "Secret Service is unavailable. Install libsecret and unlock or start a Secret Service provider, such as GNOME Keyring or KWallet, then retry."
            ],
            warnings);
    }

    private static RuntimeDependencyDiagnostics CreateDiagnostics(
        string ytDlpPath,
        string mpvPath,
        bool secretServiceAvailable,
        Func<string, bool> isExecutableAvailable)
    {
        return new RuntimeDependencyDiagnostics(
            new FixedPreferencesService(new AppPreferences
            {
                YtDlpExecutablePath = ytDlpPath,
                MpvExecutablePath = mpvPath
            }),
            new SecretServiceAvailability(secretServiceAvailable),
            isExecutableAvailable);
    }

    private sealed class FixedPreferencesService(AppPreferences preferences) : IPreferencesService
    {
        private AppPreferences _preferences = preferences;

        public event EventHandler<AppPreferences>? PreferencesChanged;

        public AppPreferences GetPreferences() => _preferences;

        public void SavePreferences(AppPreferences updatedPreferences)
        {
            _preferences = updatedPreferences;
            PreferencesChanged?.Invoke(this, _preferences);
        }
    }

    private sealed class SecretServiceAvailability(bool isAvailable) : ISecretServiceAvailability
    {
        public bool IsAvailable { get; } = isAvailable;
    }
}
