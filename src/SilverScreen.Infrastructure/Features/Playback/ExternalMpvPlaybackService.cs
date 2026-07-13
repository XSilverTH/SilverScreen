using System.ComponentModel;
using System.Diagnostics;
using SilverScreen.Core.Models;
using SilverScreen.Core.Services;
using SilverScreen.Features.Session;

namespace SilverScreen.Features.Playback;

public sealed class ExternalMpvPlaybackService : IPlaybackService
{
    private readonly PlaybackOptions _options;
    private readonly MpvCommandBuilder _commandBuilder;
    private readonly ICookieFileProvider? _cookieFileProvider;

    public ExternalMpvPlaybackService()
        : this(new PlaybackOptions(), new MpvCommandBuilder())
    {
    }

    public ExternalMpvPlaybackService(
        PlaybackOptions options,
        MpvCommandBuilder commandBuilder,
        ICookieFileProvider? cookieFileProvider = null)
    {
        _options = options;
        _commandBuilder = commandBuilder;
        _cookieFileProvider = cookieFileProvider;
    }

    public async Task<string> PlayAsync(PlaybackRequest request)
    {
        CookieFileLease? cookieFile = null;

        try
        {
            cookieFile = _cookieFileProvider?.CreateCookieFile();
            var command = _commandBuilder.Build(request, _options, cookieFile?.Path);
            LogDebug(
                $"Launching MPV. executable='{command.ExecutablePath}', manualSessionActive={cookieFile is not null}, tempCookiesProvided={cookieFile is not null}, ytdlCookiesOption={CommandUsesYtdlCookiesOption(command)}.");

            var startInfo = _commandBuilder.BuildStartInfo(command);
            var started = await Task.Run(() => Process.Start(startInfo)).ConfigureAwait(false);
            if (started is null)
            {
                LogDebug("MPV process start returned no process.");
                CleanupCookieLeaseQuietly(cookieFile, "MPV start returned no process");
                return "Could not start MPV. Is it installed?";
            }

            LogDebug($"MPV process started. pid={TryGetProcessId(started)}.");
            var cookieFileForProcess = cookieFile;
            cookieFile = null;

            _ = ObserveProcessExitAsync(started, cookieFileForProcess);

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

    private static async Task ObserveProcessExitAsync(Process process, IDisposable? cookieFileLease)
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
        {
            return null;
        }

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

    private static void LogDebug(string message)
    {
        Debug.WriteLine($"[SilverScreen] {message}");
        Console.Error.WriteLine($"[SilverScreen] {message}");
    }
}