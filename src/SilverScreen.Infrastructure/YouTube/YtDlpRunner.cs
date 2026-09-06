using System.Diagnostics;
using Serilog;
using SilverScreen.Infrastructure.Common;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace SilverScreen.Infrastructure.YouTube;

public sealed class YtDlpRunner : IYtDlpRunner
{
    private static readonly ILogger Logger = Log.ForContext<YtDlpRunner>();
    private static readonly TimeSpan KillGracePeriod = TimeSpan.FromSeconds(5);

    // Option names whose value must never reach the logs. Our own invocations only
    // pass cookie file paths (never cookie contents), but redaction is defense in depth
    // in case a future caller forwards credential-bearing options.
    private static readonly HashSet<string> SecretOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--password",
        "--client-secret",
        "--access-token",
        "--refresh-token",
        "--token",
        "--api-key"
    };

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
        Logger.Debug("Executing yt-dlp binary {FileName} with arguments {ArgumentList}", startInfo.FileName,
            RedactArgumentList(startInfo));
        if (!process.Start())
        {
            Logger.Error("Failed to start yt-dlp process using executable {FileName}", startInfo.FileName);
            throw new InvalidOperationException("yt-dlp did not start a process.");
        }

        // Stream reads observe the linked token so neither user cancellation nor the
        // timeout leaves a pump hanging after the process is gone.
        var outputTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
                Logger.Debug("yt-dlp process execution was canceled");
            else
                Logger.Warning("yt-dlp process execution timed out after {TimeoutSeconds}s", timeout.TotalSeconds);

            await KillAndDrainAsync(process, outputTask, errorTask).ConfigureAwait(false);

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

    private static async Task KillAndDrainAsync(Process process, Task<string> outputTask, Task<string> errorTask)
    {
        // Robust kill: take down the entire process tree, then bound every subsequent
        // wait so a wedged child can delay but never hang teardown.
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Kill of yt-dlp process tree was not needed or not permitted");
        }

        try
        {
            await process.WaitForExitAsync().WaitAsync(KillGracePeriod).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Bounded wait for killed yt-dlp process did not complete cleanly");
        }

        if (!HasExitedQuietly(process))
            Logger.Warning("yt-dlp process did not exit within {GraceSeconds}s of kill",
                KillGracePeriod.TotalSeconds);

        try
        {
            await Task.WhenAll(outputTask, errorTask).WaitAsync(KillGracePeriod).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The process has been terminated; stream failures are no longer actionable.
            Logger.Debug(ex, "Draining killed yt-dlp streams did not complete cleanly");
        }
    }

    private static bool HasExitedQuietly(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Could not query yt-dlp process exit state");
            return false;
        }
    }

    private static string RedactArgumentList(ProcessStartInfo startInfo)
    {
        if (startInfo.ArgumentList.Count > 0)
        {
            var redacted = new string[startInfo.ArgumentList.Count];
            for (var i = 0; i < startInfo.ArgumentList.Count; i++)
            {
                var argument = startInfo.ArgumentList[i];
                redacted[i] = IsSecretValue(i, startInfo.ArgumentList) || IsSecretAssignment(argument)
                    ? "***"
                    : argument;
            }

            return string.Join(" ", redacted);
        }

        return RedactFreeform(startInfo.Arguments);
    }

    private static bool IsSecretValue(int index, System.Collections.Generic.IList<string> arguments)
    {
        return index > 0 && SecretOptions.Contains(arguments[index - 1]);
    }

    private static bool IsSecretAssignment(string argument)
    {
        var separator = argument.IndexOf('=');
        return separator > 0 && SecretOptions.Contains(argument[..separator]);
    }

    private static string RedactFreeform(string arguments)
    {
        if (string.IsNullOrEmpty(arguments))
            return arguments;

        foreach (var secret in SecretOptions)
        {
            var index = arguments.IndexOf(secret, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                var valueStart = index + secret.Length;
                if (valueStart < arguments.Length &&
                    (arguments[valueStart] == '=' || char.IsWhiteSpace(arguments[valueStart])))
                {
                    var end = valueStart + 1;
                    var quoted = end < arguments.Length && arguments[end] == '"';
                    if (quoted) end++;
                    while (end < arguments.Length && (quoted ? arguments[end] != '"' : !char.IsWhiteSpace(arguments[end])))
                        end++;
                    if (quoted && end < arguments.Length) end++;
                    arguments = string.Concat(arguments.AsSpan(0, valueStart + 1), "***",
                        arguments.AsSpan(end));
                }

                index = arguments.IndexOf(secret, index + secret.Length, StringComparison.OrdinalIgnoreCase);
            }
        }

        return arguments;
    }
}
