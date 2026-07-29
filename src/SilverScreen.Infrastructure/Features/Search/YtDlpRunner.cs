using System.Diagnostics;

namespace SilverScreen.Infrastructure.Features.Search;

public sealed class YtDlpRunner : IYtDlpRunner
{
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
        if (!process.Start())
            throw new InvalidOperationException("yt-dlp did not start a process.");

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await DrainAndWaitForExitAsync(process, outputTask, errorTask).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"yt-dlp process timed out after {timeout.TotalSeconds:0} seconds.");
        }

        await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
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