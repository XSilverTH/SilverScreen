using System.Diagnostics;
using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Features.Session;

namespace SilverScreen.Infrastructure.Features.Search;

public sealed class YtDlpRunner(ICookieFileProvider? cookieFileProvider = null) : IYtDlpRunner, IYtDlpProcessRunner
{
    public async Task<ProcessResult> RunSearchAsync(
        SearchRequest request,
        YtDlpOptions options,
        CancellationToken cancellationToken)
    {
        using var cookieFile = cookieFileProvider?.CreateCookieFile();
        return await RunAsync(BuildSearchStartInfo(request, options, cookieFile?.Path), options.Timeout, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("yt-dlp did not start a process.");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

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

    public static ProcessStartInfo BuildSearchStartInfo(SearchRequest request, YtDlpOptions options,
        string? cookieFilePath = null)
    {
        var startInfo = CreateStartInfo(options.ExecutablePath);
        AddCommonArguments(startInfo, cookieFilePath);
        startInfo.ArgumentList.Add($"ytsearch{options.MaxResults}:{request.Query}");
        return startInfo;
    }

    public static ProcessStartInfo BuildHomeStartInfo(string executablePath, string? cookieFilePath = null)
    {
        var startInfo = CreateStartInfo(executablePath);
        AddCommonArguments(startInfo, cookieFilePath);
        startInfo.ArgumentList.Add(":ytrec");
        return startInfo;
    }

    private static ProcessStartInfo CreateStartInfo(string executablePath)
    {
        return new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
    }

    private static void AddCommonArguments(ProcessStartInfo startInfo, string? cookieFilePath)
    {
        startInfo.ArgumentList.Add("--dump-single-json");
        startInfo.ArgumentList.Add("--flat-playlist");
        startInfo.ArgumentList.Add("--skip-download");
        startInfo.ArgumentList.Add("--extractor-args");
        startInfo.ArgumentList.Add("youtubetab:approximate_date");

        if (string.IsNullOrWhiteSpace(cookieFilePath)) return;

        startInfo.ArgumentList.Add("--cookies");
        startInfo.ArgumentList.Add(cookieFilePath);
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