using System.Diagnostics;
using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Features.Session;

namespace SilverScreen.Infrastructure.Features.Search;

public sealed class YtDlpRunner(ICookieFileProvider? cookieFileProvider = null) : IYtDlpRunner
{
    public async Task<ProcessResult> RunSearchAsync(
        SearchRequest request,
        YtDlpOptions options,
        CancellationToken cancellationToken)
    {
        using var cookieFile = cookieFileProvider?.CreateCookieFile();

        var startInfo = BuildSearchStartInfo(request, options, cookieFile?.Path);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(options.Timeout);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException($"yt-dlp search timed out after {options.Timeout.TotalSeconds:0} seconds.");
        }

        return new ProcessResult(
            process.ExitCode,
            await outputTask.ConfigureAwait(false),
            await errorTask.ConfigureAwait(false));
    }

    public static ProcessStartInfo BuildSearchStartInfo(SearchRequest request, YtDlpOptions options,
        string? cookieFilePath = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = options.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        startInfo.ArgumentList.Add("--dump-single-json");
        startInfo.ArgumentList.Add("--flat-playlist");
        startInfo.ArgumentList.Add("--skip-download");

        if (!string.IsNullOrWhiteSpace(cookieFilePath))
        {
            startInfo.ArgumentList.Add("--cookies");
            startInfo.ArgumentList.Add(cookieFilePath);
        }

        startInfo.ArgumentList.Add($"ytsearch{options.MaxResults}:{request.Query}");

        return startInfo;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}