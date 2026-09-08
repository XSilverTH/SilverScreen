using System.ComponentModel;
using System.Diagnostics;
using Serilog;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Infrastructure.Player;

public sealed class ExternalMpvPlaybackService(
    IPreferencesService preferencesService,
    PlaybackCoordinator playbackCoordinator,
    YtDlpMediaResolver? mediaResolver = null)
    : IPlaybackService, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<ExternalMpvPlaybackService>();
    private static readonly TimeSpan YtdlHookFailureWindow = TimeSpan.FromSeconds(15);
    private readonly Dictionary<long, MpvIpcPlaybackObserver> _activeObservers = [];
    private readonly Lock _activeObserversLock = new();

    private readonly PlaybackCoordinator _coordinator =
        playbackCoordinator ?? throw new ArgumentNullException(nameof(playbackCoordinator));

    private readonly YtDlpMediaResolver? _mediaResolver = mediaResolver;

    private readonly IPreferencesService _preferencesService =
        preferencesService ?? throw new ArgumentNullException(nameof(preferencesService));

    private bool _disposed;

    internal ExternalMpvPlaybackService(
        IPreferencesService preferencesService,
        ICookieFileProvider? cookieFileProvider = null,
        IPlaybackPresenceService? playbackPresenceService = null,
        IYouTubePlaybackTelemetryService? playbackTelemetryService = null,
        YtDlpMediaResolver? mediaResolver = null)
        : this(
            preferencesService,
            new PlaybackCoordinator(cookieFileProvider, playbackPresenceService, playbackTelemetryService),
            mediaResolver)
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
    ///     ytdl-format, and IPC endpoint. mpv+yt-dlp fetch formats and advance the playlist.
    ///     If mpv exits fast with a non-zero code (ytdl_hook extraction failure), the current
    ///     video is resolved once via <see cref="YtDlpMediaResolver.TryResolveAsFallbackAsync" />
    ///     and mpv is relaunched a single time with the direct media URLs. Returns a status
    ///     string, never throws for empty requests or temporary-file failures (only
    ///     ArgumentNullException for a null request).
    /// </summary>
    public Task<string> PlayAsync(PlaybackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Videos.IsDefaultOrEmpty)
            return Task.FromResult(PlaybackRequest.EmptyQueueMessage);

        return LaunchMpvAsync(request, isFallbackRetry: false);
    }

    private async Task<string> LaunchMpvAsync(
        PlaybackRequest request,
        bool isFallbackRetry,
        IReadOnlyList<string>? extraArguments = null)
    {
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
            if (extraArguments is { Count: > 0 })
                command = command with { Arguments = [..command.Arguments, ..extraArguments] };
            Logger.Information(
                "Launching MPV. ExecutablePath: {ExecutablePath}; TempCookiesProvided: {TempCookiesProvided}; CookiesOption: {CookiesOption}; VideoCount: {VideoCount}; StartIndex: {StartIndex}",
                command.ExecutablePath,
                cookieFile is not null,
                CommandUsesCookiesOption(command),
                request.Videos.Length,
                request.EffectiveStartIndex);

            var startInfo = MpvCommandBuilder.BuildStartInfo(command);
            var launchTimestamp = DateTimeOffset.UtcNow;
            var started = await Task.Run(() => Process.Start(startInfo)).ConfigureAwait(false);
            if (started is null)
            {
                Logger.Warning("MPV process start returned no process");
                CleanupCookieLease(cookieFile, "MPV start returned no process");
                CleanupIpcDirectory(ipcDirectory);
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

            ObserveProcessExitAsync(started, cookieFileForProcess, playbackId, request, launchTimestamp,
                isFallbackRetry).FireAndForget(Logger);

            return "Opening in MPV.";
        }
        catch (Win32Exception ex)
        {
            Logger.Warning(ex, "MPV process start failed");
            CleanupCookieLease(cookieFile, "MPV executable start failed");
            CleanupIpcDirectory(ipcDirectory);
            return RuntimeDependencyGuidance.MpvUnavailable(activeOptions.MpvExecutablePath);
        }
        catch (InvalidOperationException ex)
        {
            Logger.Warning(ex, "MPV playback request rejected");
            CleanupCookieLease(cookieFile, "MPV playback request rejected");
            CleanupIpcDirectory(ipcDirectory);
            return ex.Message;
        }
        catch (IOException ex)
        {
            Logger.Warning(ex, "MPV temporary file setup failed");
            CleanupCookieLease(cookieFile, "MPV temporary file setup failed");
            CleanupIpcDirectory(ipcDirectory);
            return "Could not prepare temporary files for MPV playback. Try again.";
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warning(ex, "MPV temporary file setup denied");
            CleanupCookieLease(cookieFile, "MPV temporary file setup denied");
            CleanupIpcDirectory(ipcDirectory);
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
            VideoQuality = prefs.Quality.ToPersistedString(),
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
            CleanupCookieLease(cookieFileLease, "MPV process exited");
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

    private async Task ObserveProcessExitAsync(
        Process process,
        IDisposable? cookieFileLease,
        long playbackId,
        PlaybackRequest request,
        DateTimeOffset launchTimestamp,
        bool isFallbackRetry)
    {
        int? exitCode;
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
            exitCode = TryGetExitCode(process);
            CompleteActivePlayback(playbackId);
            HandleProcessExited(process, cookieFileLease);
        }

        await MaybeRetryWithFallbackAsync(request, exitCode, launchTimestamp, isFallbackRetry)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Single-shot recovery for mpv ytdl_hook extraction failures: a fast non-zero exit means
    ///     mpv never started playing, so the current video is resolved once via
    ///     <see cref="YtDlpMediaResolver.TryResolveAsFallbackAsync" /> and mpv is relaunched with
    ///     the direct media URLs through the same launch path (fresh cookie lease and IPC
    ///     directory, cleaned up exactly like the initial launch). The retry never retries itself.
    ///     Never throws.
    /// </summary>
    private async Task MaybeRetryWithFallbackAsync(
        PlaybackRequest request,
        int? exitCode,
        DateTimeOffset launchTimestamp,
        bool isFallbackRetry)
    {
        if (isFallbackRetry || exitCode is null or 0)
            return;
        if (DateTimeOffset.UtcNow - launchTimestamp > YtdlHookFailureWindow)
            return;
        if (_mediaResolver is null)
        {
            Logger.Debug("Skipping MPV fallback retry: no media resolver configured");
            return;
        }
        if (request.Videos.IsDefaultOrEmpty)
            return;

        var video = request.Videos[request.EffectiveStartIndex];
        Logger.Warning("MPV exited quickly with code {ExitCode}; attempting yt-dlp fallback for {VideoId}",
            exitCode, video.Id);

        var fallback = await _mediaResolver.TryResolveAsFallbackAsync(video.Id).ConfigureAwait(false);
        var media = fallback.IsSuccess ? fallback.Media : null;
        if (string.IsNullOrWhiteSpace(media?.VideoUrl))
        {
            Logger.Warning("MPV fallback resolution failed: {Status}", fallback.StatusMessage);
            return;
        }

        Logger.Information("MPV fallback resolved direct media for {VideoId}; relaunching once", video.Id);
        var retryRequest = new PlaybackRequest([video with { WatchUrl = media.VideoUrl }]);
        IReadOnlyList<string>? extraArguments = string.IsNullOrWhiteSpace(media.AudioUrl)
            ? null
            : [$"--audio-file={media.AudioUrl}"];
        var status = await LaunchMpvAsync(retryRequest, isFallbackRetry: true, extraArguments: extraArguments)
            .ConfigureAwait(false);
        Logger.Information("MPV fallback relaunch finished: {Status}", status);
    }

    private static void CleanupCookieLease(IDisposable? cookieFileLease, string reason)
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

    private static void CleanupIpcDirectory(DirectoryInfo? directory)
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