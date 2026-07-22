using System.ComponentModel;
using System.Diagnostics;
using Serilog;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Session;

namespace SilverScreen.Infrastructure.Features.Playback;

public sealed class ExternalMpvPlaybackService : IPlaybackService
{
    private static readonly ILogger Logger = Log.ForContext<ExternalMpvPlaybackService>();
    private readonly Lock _activePlaybackLock = new();
    private readonly Dictionary<long, ActivePlayback> _activePlaybacks = [];
    private readonly MpvCommandBuilder _commandBuilder;
    private readonly ICookieFileProvider? _cookieFileProvider;
    private readonly IPlaybackPresenceService? _playbackPresenceService;
    private readonly IPreferencesService? _preferencesService;
    private readonly PlaybackOptions _staticOptions;
    private long _nextPlaybackId;

    public ExternalMpvPlaybackService()
        : this(new PlaybackOptions(), new MpvCommandBuilder())
    {
    }

    public ExternalMpvPlaybackService(
        PlaybackOptions options,
        MpvCommandBuilder commandBuilder,
        ICookieFileProvider? cookieFileProvider = null,
        IPlaybackPresenceService? playbackPresenceService = null)
    {
        _staticOptions = options;
        _commandBuilder = commandBuilder;
        _cookieFileProvider = cookieFileProvider;
        _preferencesService = null;
        _playbackPresenceService = playbackPresenceService;
    }

    public ExternalMpvPlaybackService(
        IPreferencesService preferencesService,
        MpvCommandBuilder commandBuilder,
        ICookieFileProvider? cookieFileProvider = null,
        IPlaybackPresenceService? playbackPresenceService = null)
    {
        _staticOptions = new PlaybackOptions();
        _commandBuilder = commandBuilder;
        _cookieFileProvider = cookieFileProvider;
        _preferencesService = preferencesService;
        _playbackPresenceService = playbackPresenceService;
    }

    public async Task<string> PlayAsync(PlaybackRequest request)
    {
        CookieFileLease? cookieFile = null;
        var activeOptions = _staticOptions;

        try
        {
            cookieFile = _cookieFileProvider?.CreateCookieFile();
            activeOptions = GetActiveOptions();
            var command = MpvCommandBuilder.Build(request, activeOptions, cookieFile?.Path);
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
                return RuntimeDependencyGuidance.MpvUnavailable(activeOptions.MpvExecutablePath);
            }

            Logger.Information("MPV process started. ProcessId: {ProcessId}", TryGetProcessId(started));
            var playbackId = RegisterActivePlayback(request, DateTimeOffset.UtcNow);
            var cookieFileForProcess = cookieFile;
            cookieFile = null;

            _ = ObserveProcessExitAsync(started, cookieFileForProcess, playbackId);

            return "Opening in MPV.";
        }
        catch (Win32Exception ex)
        {
            Logger.Warning(ex, "MPV process start failed");
            CleanupCookieLeaseQuietly(cookieFile, "MPV executable start failed");
            return RuntimeDependencyGuidance.MpvUnavailable(activeOptions.MpvExecutablePath);
        }
        catch (InvalidOperationException ex)
        {
            Logger.Warning(ex, "MPV playback request rejected");
            CleanupCookieLeaseQuietly(cookieFile, "MPV playback request rejected");
            return ex.Message;
        }
    }

    private PlaybackOptions GetActiveOptions()
    {
        if (_preferencesService is null) return _staticOptions;
        var prefs = _preferencesService.GetPreferences();
        return new PlaybackOptions
        {
            MpvExecutablePath = prefs.MpvExecutablePath,
            VideoQuality = prefs.VideoQuality,
            MarkWatchedVideos = prefs.MarkWatchedVideos,
            ExternalMpvEnabled = _staticOptions.ExternalMpvEnabled
        };
    }

    internal long RegisterActivePlayback(PlaybackRequest request, DateTimeOffset startedAt)
    {
        lock (_activePlaybackLock)
        {
            var playback = new ActivePlayback(++_nextPlaybackId, request, startedAt);
            _activePlaybacks.Add(playback.Id, playback);
            SetPresenceQuietly(playback.Request, playback.StartedAt);
            return playback.Id;
        }
    }

    internal void CompleteActivePlayback(long playbackId)
    {
        lock (_activePlaybackLock)
        {
            if (!_activePlaybacks.TryGetValue(playbackId, out var completedPlayback)) return;

            var wasMostRecent = _activePlaybacks.Keys.Max() == completedPlayback.Id;
            _activePlaybacks.Remove(playbackId);
            if (!wasMostRecent) return;

            if (_activePlaybacks.Count == 0)
            {
                ClearPresenceQuietly();
                return;
            }

            var currentPlayback = _activePlaybacks.Values.MaxBy(playback => playback.Id)!;
            SetPresenceQuietly(currentPlayback.Request, currentPlayback.StartedAt);
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

    private void SetPresenceQuietly(PlaybackRequest request, DateTimeOffset startedAt)
    {
        if (_playbackPresenceService is null) return;

        try
        {
            _playbackPresenceService.SetPlaying(request, startedAt);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Playback presence update failed safely");
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

    private sealed record ActivePlayback(long Id, PlaybackRequest Request, DateTimeOffset StartedAt);
}