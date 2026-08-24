using SilverScreen.Infrastructure.Common;
using System.Diagnostics;
using Serilog;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace SilverScreen.Infrastructure.YouTube;

public sealed class YtDlpRunner : IYtDlpRunner
{
    private static readonly ILogger Logger = Log.ForContext<YtDlpRunner>();

    public async Task<ProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        using var process = new Process();
        process.StartInfo = startInfo;
        Logger.Debug("Executing yt-dlp binary {FileName} with arguments {Arguments}", startInfo.FileName,
            startInfo.Arguments);
        if (!process.Start())
        {
            Logger.Error("Failed to start yt-dlp process using executable {FileName}", startInfo.FileName);
            throw new InvalidOperationException("yt-dlp did not start a process.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Logger.Warning("yt-dlp process execution timed out after {TimeoutSeconds}s or was canceled",
                timeout.TotalSeconds);
            TryKill(process);
            await DrainAndWaitForExitAsync(process, outputTask, errorTask).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"yt-dlp process timed out after {timeout.TotalSeconds:0} seconds.");
        }

        await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
        if (process.ExitCode != 0)
            Logger.Warning("yt-dlp process exited with non-zero exit code {ExitCode}", process.ExitCode);
        else
            Logger.Debug("yt-dlp process exited successfully (ExitCode 0)");

        return new ProcessResult(process.ExitCode, outputTask.Result, errorTask.Result);
    }


    private static async Task DrainAndWaitForExitAsync(Process process, Task<string> outputTask, Task<string> errorTask)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The process has been terminated; stream failures are no longer actionable.
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}