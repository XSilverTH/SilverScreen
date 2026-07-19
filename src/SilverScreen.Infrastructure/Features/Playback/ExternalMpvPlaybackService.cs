using System.ComponentModel;
using System.Diagnostics;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Infrastructure.Features.Session;

namespace SilverScreen.Infrastructure.Features.Playback;

public sealed class ExternalMpvPlaybackService : IPlaybackService
{
    private readonly MpvCommandBuilder _commandBuilder;
    private readonly ICookieFileProvider? _cookieFileProvider;
    private readonly IPreferencesService? _preferencesService;
    private readonly PlaybackOptions _staticOptions;
    private readonly IPlaybackPresenceService? _playbackPresenceService;
    private readonly object _activePlaybackLock = new();
    private readonly Dictionary<long, ActivePlayback> _activePlaybacks = [];
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

        try
        {
            cookieFile = _cookieFileProvider?.CreateCookieFile();
            var activeOptions = GetActiveOptions();
            var command = MpvCommandBuilder.Build(request, activeOptions, cookieFile?.Path);
            LogDebug(
                $"Launching MPV. executable='{command.ExecutablePath}', manualSessionActive={cookieFile is not null}, tempCookiesProvided={cookieFile is not null}, ytdlCookiesOption={CommandUsesYtdlCookiesOption(command)}.");

            var startInfo = MpvCommandBuilder.BuildStartInfo(command);
            var started = await Task.Run(() => Process.Start(startInfo)).ConfigureAwait(false);
            if (started is null)
            {
                LogDebug("MPV process start returned no process.");
                CleanupCookieLeaseQuietly(cookieFile, "MPV start returned no process");
                return "Could not start MPV. Is it installed?";
            }

            LogDebug($"MPV process started. pid={TryGetProcessId(started)}.");
            var playbackId = RegisterActivePlayback(request, DateTimeOffset.UtcNow);
            var cookieFileForProcess = cookieFile;
            cookieFile = null;

            _ = ObserveProcessExitAsync(started, cookieFileForProcess, playbackId);

            return "Opening in MPV.";
        }
        catch (Win32Exception ex)
        {
            LogDebug($"MPV process start failed. error={ex.GetType().Name}: {ex.Message}");
            CleanupCookieLeaseQuietly(cookieFile, "MPV executable start failed");
            return "Could not start MPV. Is it installed?";
        }
        catch (InvalidOperationException ex)
        {
            LogDebug($"MPV playback request rejected. error={ex.GetType().Name}: {ex.Message}");
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

    internal static void HandleProcessExited(Process? process, IDisposable? cookieFileLease)
    {
        try
        {
            var exitCode = TryGetExitCode(process);
            LogDebug(exitCode is null ? "MPV exited; exit code unavailable." : $"MPV exited with code {exitCode}.");
            CleanupCookieLeaseQuietly(cookieFileLease, "MPV process exited");
        }
        catch (Exception ex)
        {
            LogDebug($"MPV exit cleanup handler failed safely. error={ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try
            {
                process?.Dispose();
            }
            catch (Exception ex)
            {
                LogDebug($"MPV process disposal failed safely. error={ex.GetType().Name}: {ex.Message}");
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
            LogDebug($"Could not observe MPV exit. error={ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            CompleteActivePlayback(playbackId);
            HandleProcessExited(process, cookieFileLease);
        }
    }

    internal static void CleanupCookieLeaseQuietly(IDisposable? cookieFileLease, string reason)
    {
        if (cookieFileLease is null)
        {
            LogDebug($"No temporary cookie file lease to clean up. reason='{reason}'.");
            return;
        }

        try
        {
            cookieFileLease.Dispose();
            LogDebug($"Temporary cookie file lease cleaned up. reason='{reason}'.");
        }
        catch (Exception ex)
        {
            LogDebug(
                $"Temporary cookie file lease cleanup failed safely. reason='{reason}', error={ex.GetType().Name}: {ex.Message}");
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
            LogDebug($"Playback presence update failed safely. error={ex.GetType().Name}: {ex.Message}");
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
            LogDebug($"Playback presence clear failed safely. error={ex.GetType().Name}: {ex.Message}");
        }
    }

    private sealed record ActivePlayback(long Id, PlaybackRequest Request, DateTimeOffset StartedAt);

    private static void LogDebug(string message)
    {
        Debug.WriteLine($"[SilverScreen] {message}");
        Console.Error.WriteLine($"[SilverScreen] {message}");
    }
}