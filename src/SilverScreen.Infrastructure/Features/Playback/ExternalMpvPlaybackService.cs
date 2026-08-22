using System.ComponentModel;
using System.Diagnostics;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;

namespace SilverScreen.Infrastructure.Features.Playback;

public sealed class ExternalMpvPlaybackService : IPlaybackService, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<ExternalMpvPlaybackService>();
    private readonly Lock _activePlaybackLock = new();
    private readonly Dictionary<long, ActivePlayback> _activePlaybacks = [];
    private readonly ICookieFileProvider? _cookieFileProvider;
    private readonly IPlaybackPresenceService? _playbackPresenceService;
    private readonly IYouTubePlaybackTelemetryService? _playbackTelemetryService;
    private readonly IWatchProgressService? _watchProgressService;
    private readonly IPreferencesService _preferencesService;
    private bool _disposed;
    private long _nextPlaybackId;

    public ExternalMpvPlaybackService(
        IPreferencesService preferencesService,
        ICookieFileProvider? cookieFileProvider = null,
        IPlaybackPresenceService? playbackPresenceService = null,
        IYouTubePlaybackTelemetryService? playbackTelemetryService = null,
        IWatchProgressService? watchProgressService = null)
    {
        _preferencesService = preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));
        _cookieFileProvider = cookieFileProvider;
        _playbackPresenceService = playbackPresenceService;
        _playbackTelemetryService = playbackTelemetryService;
        _watchProgressService = watchProgressService;
    }

    public void Dispose()
    {
        lock (_activePlaybackLock)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var playback in _activePlaybacks.Values)
            {
                playback.Observer?.Dispose();
                playback.Telemetry?.Dispose();
            }
        }
    }

    public async Task<string> PlayAsync(PlaybackRequest request)
    {
        CookieFileLease? cookieFile = null;
        DirectoryInfo? ipcDirectory = null;
        var activeOptions = GetActiveOptions();

        try
        {
            cookieFile = _cookieFileProvider?.CreateCookieFile();
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
        lock (_activePlaybackLock)
        {
            var playback = new ActivePlayback(++_nextPlaybackId, request, StartTelemetryQuietly(request));
            _activePlaybacks.Add(playback.Id, playback);
            return playback.Id;
        }
    }

    internal void UpdateActivePlayback(long playbackId, PlaybackPresenceState state)
    {
        lock (_activePlaybackLock)
        {
            if (!_activePlaybacks.TryGetValue(playbackId, out var playback)) return;

            playback.State = state;
            SetTelemetryQuietly(playback.Telemetry, state);
            _watchProgressService?.Update(playback.Request, state);
            if (_activePlaybacks.Keys.Max() == playbackId) SetPresenceQuietly(playback.Request, state);
        }
    }

    private void AttachObserver(long playbackId, MpvIpcPlaybackObserver observer)
    {
        lock (_activePlaybackLock)
        {
            if (_activePlaybacks.TryGetValue(playbackId, out var playback))
                playback.Observer = observer;
            else
                observer.Dispose();
        }
    }

    internal void CompleteActivePlayback(long playbackId)
    {
        lock (_activePlaybackLock)
        {
            if (!_activePlaybacks.TryGetValue(playbackId, out var completedPlayback)) return;

            var wasMostRecent = _activePlaybacks.Keys.Max() == completedPlayback.Id;
            _activePlaybacks.Remove(playbackId);
            completedPlayback.Observer?.Dispose();
            completedPlayback.Telemetry?.Dispose();
            if (!wasMostRecent) return;

            var currentPlayback = _activePlaybacks.Values.MaxBy(playback => playback.Id);
            if (currentPlayback?.State is { } state)
                SetPresenceQuietly(currentPlayback.Request, state);
            else
                ClearPresenceQuietly();
        }
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

    private void SetPresenceQuietly(PlaybackRequest request, PlaybackPresenceState state)
    {
        if (_playbackPresenceService is null) return;

        try
        {
            _playbackPresenceService.SetPlaybackState(request, state);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Playback presence update failed safely");
        }
    }

    private IYouTubePlaybackTelemetrySession? StartTelemetryQuietly(PlaybackRequest request)
    {
        if (_playbackTelemetryService is null) return null;
        try
        {
            return _playbackTelemetryService.Start(request);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "YouTube playback telemetry start failed safely");
            return null;
        }
    }

    private static void SetTelemetryQuietly(IYouTubePlaybackTelemetrySession? telemetry,
        PlaybackPresenceState state)
    {
        if (telemetry is null) return;
        try
        {
            telemetry.UpdateState(state);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "YouTube playback telemetry update failed safely");
        }
    }

    private void ClearPresenceQuietly()
    {
        if (_playbackPresenceService is null) return;

        try
        {
            _playbackPresenceService.Clear();
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Playback presence clear failed safely");
        }
    }

    private static void CleanupIpcDirectoryQuietly(DirectoryInfo? directory)
    {
        if (directory is null) return;
        try
        {
            directory.Delete(true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class ActivePlayback(long id, PlaybackRequest request, IYouTubePlaybackTelemetrySession? telemetry)
    {
        public long Id { get; } = id;
        public PlaybackRequest Request { get; } = request;
        public IYouTubePlaybackTelemetrySession? Telemetry { get; } = telemetry;
        public IDisposable? Observer { get; set; }
        public PlaybackPresenceState? State { get; set; }
    }
}