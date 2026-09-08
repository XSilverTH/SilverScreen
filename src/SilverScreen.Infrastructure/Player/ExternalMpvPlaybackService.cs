using System.ComponentModel;
using System.Diagnostics;
using Serilog;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;

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
        IYouTubePlaybackTelemetryService? playbackTelemetryService = null)
        : this(
            preferencesService,
            new PlaybackCoordinator(cookieFileProvider, playbackPresenceService, playbackTelemetryService))
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

    /// <summary>
    ///     Launches external mpv with the FULL queue snapshot as watch URLs plus cookie lease,
    ///     ytdl-format, and IPC endpoint. mpv+yt-dlp fetch formats and advance the playlist;
    ///     resolved direct URLs are never used here. Returns a status string, never throws for
    ///     empty requests or temporary-file failures (only ArgumentNullException for a null request).
    /// </summary>
    public async Task<string> PlayAsync(PlaybackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Videos.IsDefaultOrEmpty)
            return PlaybackRequest.EmptyQueueMessage;

        CookieFileLease? cookieFile = null;
        DirectoryInfo? ipcDirectory = null;
        var activeOptions = GetActiveOptions();

        try
        {
            // Never throws: TemporaryCookieFile.CreateLease returns null on temp-create failure.
            cookieFile = _coordinator.AcquireCookieFileLease();
            activeOptions = GetActiveOptions();
            ipcDirectory = Directory.CreateTempSubdirectory("silverscreen-mpv-");
            var ipcEndpoint = Path.Combine(ipcDirectory.FullName, "mpv.sock");

            var command =
                MpvCommandBuilder.Build(request, activeOptions, cookieFile?.Path, ipcEndpoint);
            Logger.Information(
                "Launching MPV. ExecutablePath: {ExecutablePath}; TempCookiesProvided: {TempCookiesProvided}; CookiesOption: {CookiesOption}; VideoCount: {VideoCount}; StartIndex: {StartIndex}",
                command.ExecutablePath,
                cookieFile is not null,
                CommandUsesCookiesOption(command),
                request.Videos.Length,
                request.EffectiveStartIndex);

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
        catch (IOException ex)
        {
            Logger.Warning(ex, "MPV temporary file setup failed");
            CleanupCookieLeaseQuietly(cookieFile, "MPV temporary file setup failed");
            CleanupIpcDirectoryQuietly(ipcDirectory);
            return "Could not prepare temporary files for MPV playback. Try again.";
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warning(ex, "MPV temporary file setup denied");
            CleanupCookieLeaseQuietly(cookieFile, "MPV temporary file setup denied");
            CleanupIpcDirectoryQuietly(ipcDirectory);
            return "Could not prepare temporary files for MPV playback. Try again.";
        }
    }

    private PlaybackOptions GetActiveOptions()
    {
        var prefs = _preferencesService.GetPreferences();
        return new PlaybackOptions
        {
            MpvExecutablePath = prefs.MpvExecutablePath,
            YtDlpExecutablePath = prefs.YtDlpExecutablePath,
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
            if (_disposed)
            {
                observer.Dispose();
                return;
            }

            _activeObservers[playbackId] = observer;
        }
    }

    internal void CompleteActivePlayback(long playbackId)
    {
        lock (_activeObserversLock)
        {
            if (_activeObservers.Remove(playbackId, out var observer)) observer.Dispose();
        }

        _coordinator.CompleteActivePlayback(playbackId);
    }

    private static void HandleProcessExited(Process? process, IDisposable? cookieFileLease)
    {
        try
        {
            var exitCode = TryGetExitCode(process);
            if (exitCode is { } code)
                Logger.Information("MPV process exited. ProcessId: {ProcessId}; ExitCode: {ExitCode}",
                    process is not null ? TryGetProcessId(process) : null,
                    code);
            else
                Logger.Information("MPV process exited. ProcessId: {ProcessId}",
                    process is not null ? TryGetProcessId(process) : null);
        }
        finally
        {
            CleanupCookieLeaseQuietly(cookieFileLease, "MPV process exited");
            try
            {
                process?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to dispose MPV process instance");
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
            Logger.Warning(ex, "Failed while waiting for MPV process exit");
        }
        finally
        {
            CompleteActivePlayback(playbackId);
            HandleProcessExited(process, cookieFileLease);
        }
    }

    private static void CleanupCookieLeaseQuietly(IDisposable? cookieFileLease, string reason)
    {
        if (cookieFileLease is null) return;
        try
        {
            cookieFileLease.Dispose();
            Logger.Information("Cleaned up temporary MPV cookie lease ({Reason})", reason);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to clean up temporary MPV cookie file ({Reason})", reason);
        }
    }

    private static int? TryGetExitCode(Process? process)
    {
        if (process is null) return null;
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to read exit code from MPV process");
            return null;
        }
    }

    private static int? TryGetProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to read PID from MPV process");
            return null;
        }
    }

    private static bool CommandUsesCookiesOption(MpvPlaybackCommand command)
    {
        return command.Arguments.Any(argument =>
            argument.Equals("--cookies", StringComparison.OrdinalIgnoreCase)
            || argument.StartsWith("--cookies-file=", StringComparison.OrdinalIgnoreCase));
    }

    private static void CleanupIpcDirectoryQuietly(DirectoryInfo? directory)
    {
        if (directory is null) return;
        try
        {
            if (directory.Exists) directory.Delete(true);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to clean up MPV IPC directory {Directory}", directory.FullName);
        }
    }
}