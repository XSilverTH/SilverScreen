using Serilog;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Player;

namespace SilverScreen.Infrastructure.Common;

/// <summary>
/// Checks whether external runtime dependencies configured for SilverScreen are reachable and executable.
/// </summary>
/// <remarks>
/// <para>
/// <b>Platform Support & Arch Linux x86-64 Tier-1 Target:</b>
/// SilverScreen targets modern 64-bit Linux, with <b>Arch Linux x86-64</b> designated as the primary tier-1 reference platform.
/// Dependencies such as <c>yt-dlp</c>, <c>mpv</c>, <c>libmpv.so.2</c>, and <c>libsecret-1.so.0</c> are expected to follow standard
/// Arch package layouts and system search paths (<c>/usr/bin</c>, <c>/usr/lib</c>).
/// </para>
/// <para>
/// <b>Defensive Diagnostics:</b>
/// All checks verify both file existence and POSIX executable permissions (<see cref="UnixFileMode.UserExecute"/>,
/// <see cref="UnixFileMode.GroupExecute"/>, <see cref="UnixFileMode.OtherExecute"/>) defensively, tolerating path lookup failures
/// without crashing, and issuing structured warnings rather than fatal application startup aborts.
/// </para>
/// </remarks>
public sealed class RuntimeDependencyDiagnostics
{
    private static readonly ILogger Logger = Log.ForContext<RuntimeDependencyDiagnostics>();
    private readonly Func<string, bool> _isExecutableAvailable;
    private readonly Func<bool> _isLibMpvAvailable;
    private readonly IPreferencesService _preferencesService;
    private readonly ISecretServiceAvailability _secretServiceAvailability;

    public RuntimeDependencyDiagnostics(
        IPreferencesService preferencesService,
        ISecretServiceAvailability secretServiceAvailability)
        : this(preferencesService, secretServiceAvailability, IsExecutableAvailable, LibMpvNative.IsAvailable)
    {
    }

    internal RuntimeDependencyDiagnostics(
        IPreferencesService preferencesService,
        ISecretServiceAvailability secretServiceAvailability,
        Func<string, bool> isExecutableAvailable,
        Func<bool> isLibMpvAvailable)
    {
        _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        _secretServiceAvailability = secretServiceAvailability ??
                                     throw new ArgumentNullException(nameof(secretServiceAvailability));
        _isExecutableAvailable =
            isExecutableAvailable ?? throw new ArgumentNullException(nameof(isExecutableAvailable));
        _isLibMpvAvailable = isLibMpvAvailable ?? throw new ArgumentNullException(nameof(isLibMpvAvailable));
    }

    /// <summary>Returns actionable setup warnings for dependencies unavailable at application startup.</summary>
    public IReadOnlyList<string> GetStartupWarnings()
    {
        var preferences = _preferencesService.GetPreferences();
        var warnings = new List<string>(3);

        if (!_isExecutableAvailable(preferences.YtDlpExecutablePath))
        {
            var msg = RuntimeDependencyGuidance.YtDlpUnavailable(preferences.YtDlpExecutablePath);
            Logger.Warning("Startup dependency warning: {Warning}", msg);
            warnings.Add(msg);
        }

        if (PlaybackBackends.IsEmbedded(preferences.PlaybackBackend))
        {
            if (!_isLibMpvAvailable())
            {
                const string libMpvMsg = RuntimeDependencyGuidance.LibMpvUnavailable;
                Logger.Warning("Startup dependency warning: {Warning}", libMpvMsg);
                warnings.Add(libMpvMsg);
            }
        }
        else if (!_isExecutableAvailable(preferences.MpvExecutablePath))
        {
            var mpvMsg = RuntimeDependencyGuidance.MpvUnavailable(preferences.MpvExecutablePath);
            Logger.Warning("Startup dependency warning: {Warning}", mpvMsg);
            warnings.Add(mpvMsg);
        }

        if (!_secretServiceAvailability.IsAvailable)
        {
            const string secretMsg = RuntimeDependencyGuidance.SecretServiceUnavailable;
            Logger.Warning("Startup dependency warning: {Warning}", secretMsg);
            warnings.Add(secretMsg);
        }

        if (warnings.Count == 0) Logger.Information("All runtime dependencies are verified and available");

        return warnings;
    }

    private static bool IsExecutableAvailable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;

        var trimmedPath = executablePath.Trim();
        if (Path.IsPathFullyQualified(trimmedPath) ||
            trimmedPath.Contains(Path.DirectorySeparatorChar) ||
            trimmedPath.Contains(Path.AltDirectorySeparatorChar))
            return IsExecutableFile(trimmedPath);

        var searchPath = Environment.GetEnvironmentVariable("PATH");
        return !string.IsNullOrWhiteSpace(searchPath) && searchPath.Split(Path.PathSeparator)
            .Select(directory =>
                Path.Combine(string.IsNullOrEmpty(directory) ? Environment.CurrentDirectory : directory, trimmedPath))
            .Any(IsExecutableFile);
    }

    private static bool IsExecutableFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            if (OperatingSystem.IsWindows()) return true;

            var mode = File.GetUnixFileMode(path);
            const UnixFileMode executeBits = UnixFileMode.UserExecute | UnixFileMode.GroupExecute |
                                             UnixFileMode.OtherExecute;
            return (mode & executeBits) != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}