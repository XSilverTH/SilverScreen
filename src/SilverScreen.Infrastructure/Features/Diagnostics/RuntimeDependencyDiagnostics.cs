using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Playback;

namespace SilverScreen.Infrastructure.Features.Diagnostics;

/// <summary>Checks whether the runtime dependencies configured for SilverScreen can be reached.</summary>
public sealed class RuntimeDependencyDiagnostics
{
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
            warnings.Add(RuntimeDependencyGuidance.YtDlpUnavailable(preferences.YtDlpExecutablePath));

        if (preferences.PlaybackBackend == PlaybackBackends.ExternalMpv &&
            !_isExecutableAvailable(preferences.MpvExecutablePath))
            warnings.Add(RuntimeDependencyGuidance.MpvUnavailable(preferences.MpvExecutablePath));
        else if (preferences.PlaybackBackend == PlaybackBackends.EmbeddedPlayer && !_isLibMpvAvailable())
            warnings.Add(RuntimeDependencyGuidance.LibMpvUnavailable);

        if (!_secretServiceAvailability.IsAvailable)
            warnings.Add(RuntimeDependencyGuidance.SecretServiceUnavailable);

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