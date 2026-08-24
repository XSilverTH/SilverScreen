using System.ComponentModel;
using System.Diagnostics;
using Serilog;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Common;

namespace SilverScreen.Infrastructure.Player;

public sealed class ExternalMpvPlaybackService(
    IPreferencesService preferencesService,
    PlaybackCoordinator playbackCoordinator)
    : IPlaybackService, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<ExternalMpvPlaybackService>();
    private readonly Dictionary<long, MpvIpcPlaybackObserver> _activeObservers = [];
    private readonly Lock _activeObserversLock = new();

    private readonly PlaybackCoordinator _coordinator =
        playbackCoordinator ?? throw new ArgumentNullException(nameof(playbackCoordinator));

    private readonly IPreferencesService _preferencesService =
        preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));

    private bool _disposed;

    internal ExternalMpvPlaybackService(
        IPreferencesService preferencesService,
        ICookieFileProvider? cookieFileProvider = null,
        IPlaybackPresenceService? playbackPresenceService = null,
        IYouTubePlaybackTelemetryService? playbackTelemetryService = null,
        IWatchProgressService? watchProgressService = null)
        : this(
            preferencesService,
            new PlaybackCoordinator(cookieFileProvider, playbackPresenceService, playbackTelemetryService,
                watchProgressService))
    {
    }

    public void Dispose()
    {
        lock (_activeObserversLock)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var observer in _activeObservers.Values) observer.Dispose();
            _activeObservers.Clear();
        }

        _coordinator.Dispose();
    }

    public async Task<string> PlayAsync(PlaybackRequest request)
    {
        CookieFileLease? cookieFile = null;
        DirectoryInfo? ipcDirectory = null;
        var activeOptions = GetActiveOptions();

        try
        {
            cookieFile = _coordinator.AcquireCookieFileLease();
            activeOptions = GetActiveOptions();
            ipcDirectory = Directory.CreateTempSubdirectory("silverscreen-mpv-");
            var ipcEndpoint = Path.Combine(ipcDirectory.FullName, "mpv.sock");
            var command = MpvCommandBuilder.Build(request, activeOptions, cookieFile?.Path, ipcEndpoint);
            Logger.Information(
                "Launching MPV. ExecutablePath: {ExecutablePath}; ManualSessionActive: {ManualSessionActive}; TempCookiesProvided: {TempCookiesProvided}; YtdlCookiesOption: {YtdlCookiesOption}",
                command.ExecutablePath,
                cookieFile is not null,
                cookieFile is not null,
                CommandUsesYtdlCookiesOption(command));

            var startInfo = MpvCommandBuilder.BuildStartInfo(command);
            var started = await Task.Run(() => Process.Start(startInfo)).ConfigureAwait(false);
            if (started is null)
            {
                Logger.Warning("MPV process start returned no process");
                CleanupCookieLeaseQuietly(cookieFile, "MPV start returned no process");
                CleanupIpcDirectoryQuietly(ipcDirectory);
                return RuntimeDependencyGuidance.MpvUnavailable(activeOptions.MpvExecutablePath);
            }

            Logger.Information("MPV process started. ProcessId: {ProcessId}", TryGetProcessId(started));
            var playbackId = RegisterActivePlayback(request);
            var observer = new MpvIpcPlaybackObserver(started, ipcEndpoint, ipcDirectory,
                state => UpdateActivePlayback(playbackId, state));
            AttachObserver(playbackId, observer);
            ipcDirectory = null;
            var cookieFileForProcess = cookieFile;
            cookieFile = null;

            ObserveProcessExitAsync(started, cookieFileForProcess, playbackId).FireAndForget(Logger);

            return "Opening in MPV.";
        }
        catch (Win32Exception ex)
        {
            Logger.Warning(ex, "MPV process start failed");
            CleanupCookieLeaseQuietly(cookieFile, "MPV executable start failed");
            CleanupIpcDirectoryQuietly(ipcDirectory);
            return RuntimeDependencyGuidance.MpvUnavailable(activeOptions.MpvExecutablePath);
        }
        catch (InvalidOperationException ex)
        {
            Logger.Warning(ex, "MPV playback request rejected");
            CleanupCookieLeaseQuietly(cookieFile, "MPV playback request rejected");
            CleanupIpcDirectoryQuietly(ipcDirectory);
            return ex.Message;
        }
    }

    private PlaybackOptions GetActiveOptions()
    {
        var prefs = _preferencesService.GetPreferences();
        return new PlaybackOptions
        {
            MpvExecutablePath = prefs.MpvExecutablePath,
            VideoQuality = prefs.VideoQuality,
            MarkWatchedVideos = prefs is { MarkWatchedVideos: true, YouTubePlaybackTelemetryEnabled: false },
            Fullscreen = prefs.OpenInFullscreen,
            AutoAdvanceNextVideo = prefs.AutoAdvanceNextVideo,
            ExternalMpvEnabled = true
        };
    }

    internal long RegisterActivePlayback(PlaybackRequest request)
    {
        return _coordinator.RegisterActivePlayback(request);
    }

    internal void UpdateActivePlayback(long playbackId, PlaybackPresenceState state)
    {
        _coordinator.UpdateActivePlayback(playbackId, state);
    }

    private void AttachObserver(long playbackId, MpvIpcPlaybackObserver observer)
    {
        lock (_activeObserversLock)
        {
            if (!_disposed)
                _activeObservers[playbackId] = observer;
            else
                observer.Dispose();
        }
    }

    internal void CompleteActivePlayback(long playbackId)
    {
        lock (_activeObserversLock)
        {
            if (_activeObservers.Remove(playbackId, out var observer))
                observer.Dispose();
        }

        _coordinator.CompleteActivePlayback(playbackId);
    }

    private static void HandleProcessExited(Process? process, IDisposable? cookieFileLease)
    {
        try
        {
            var exitCode = TryGetExitCode(process);
            if (exitCode is null)
                Logger.Information("MPV exited; exit code unavailable");
            else
                Logger.Information("MPV exited with code {ExitCode}", exitCode);
            CleanupCookieLeaseQuietly(cookieFileLease, "MPV process exited");
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "MPV exit cleanup handler failed safely");
        }
        finally
        {
            try
            {
                process?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "MPV process disposal failed safely");
            }
        }
    }

    private async Task ObserveProcessExitAsync(Process process, IDisposable? cookieFileLease, long playbackId)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Could not observe MPV exit");
        }
        finally
        {
            CompleteActivePlayback(playbackId);
            HandleProcessExited(process, cookieFileLease);
        }
    }

    private static void CleanupCookieLeaseQuietly(IDisposable? cookieFileLease, string reason)
    {
        if (cookieFileLease is null)
        {
            Logger.Debug(
                "No temporary cookie file lease to clean up. Reason: {Reason}",
                reason);
            return;
        }

        try
        {
            cookieFileLease.Dispose();
            Logger.Information(
                "Temporary cookie file lease cleaned up. Reason: {Reason}",
                reason);
        }
        catch (Exception ex)
        {
            Logger.Warning(
                ex,
                "Temporary cookie file lease cleanup failed safely. Reason: {Reason}",
                reason);
        }
    }

    private static int? TryGetExitCode(Process? process)
    {
        if (process is null)
            return null;

        try
        {
            return process.ExitCode;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            return null;
        }
    }

    private static int? TryGetProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            return null;
        }
    }

    private static bool CommandUsesYtdlCookiesOption(MpvPlaybackCommand command)
    {
        return command.Arguments.Any(argument =>
            argument.StartsWith("--ytdl-raw-options=cookies=", StringComparison.Ordinal));
    }

    private static void CleanupIpcDirectoryQuietly(DirectoryInfo? directory)
    {
        if (directory is null) return;
        try
        {
            if (directory.Exists) directory.Delete(true);
        }
        catch
        {
            // ignored
        }
    }
}