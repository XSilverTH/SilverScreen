using System.Diagnostics;
using SilverScreen.Infrastructure.Features.Search;

namespace SilverScreen.Tests;

public sealed class YtDlpRunnerTests
{
    [Fact]
    public void BuildHomeStartInfoUsesSharedArgumentsAndOptionalCookies()
    {
        var startInfo = YtDlpRunner.BuildHomeStartInfo("yt-dlp", "/tmp/cookies.txt");

        Assert.Collection(
            startInfo.ArgumentList,
            argument => Assert.Equal("--dump-single-json", argument),
            argument => Assert.Equal("--flat-playlist", argument),
            argument => Assert.Equal("--skip-download", argument),
            argument => Assert.Equal("--extractor-args", argument),
            argument => Assert.Equal("youtubetab:approximate_date", argument),
            argument => Assert.Equal("--cookies", argument),
            argument => Assert.Equal("/tmp/cookies.txt", argument),
            argument => Assert.Equal(":ytrec", argument));
    }

    [Fact]
    public async Task RunAsync_OnTimeoutTerminatesTheProcessBeforeFailing()
    {
        var pidPath = Path.GetTempFileName();
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add($"echo $$ > \"{pidPath}\"; sleep 5");

            var runner = new YtDlpRunner();

            await Assert.ThrowsAsync<TimeoutException>(() =>
                runner.RunAsync(startInfo, TimeSpan.FromSeconds(1), CancellationToken.None));

            var processId = int.Parse(await File.ReadAllTextAsync(pidPath));
            Assert.Throws<ArgumentException>(() => Process.GetProcessById(processId));
        }
        finally
        {
            File.Delete(pidPath);
        }
    }
}
