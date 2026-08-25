using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests.YouTube;

public sealed class YtDlpProcessHostTests
{
    [Fact]
    public async Task EnsureStartedAsync_WithSystemYtDlp_StartsProcessAndCapturesVersion()
    {
        await using var host = new YtDlpProcessHost();
        await host.EnsureStartedAsync("yt-dlp", CancellationToken.None);

        Assert.True(host.IsRunning);
        Assert.False(string.IsNullOrWhiteSpace(host.Version));
        Assert.False(string.IsNullOrWhiteSpace(host.PythonVersion));
    }

    [Fact]
    public async Task RunAsync_WithInvalidArguments_ReturnsNonZeroExitCodeWithoutCrashing()
    {
        await using var host = new YtDlpProcessHost();
        var result = await host.RunAsync("yt-dlp", ["--completely-invalid-option-xyz"], TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("error", result.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.True(host.IsRunning);
    }

    [Fact]
    public async Task RunAsync_WithSearchQuery_ReturnsValidVideoJson()
    {
        await using var host = new YtDlpProcessHost();
        var args = new[]
        {
            "--dump-single-json",
            "--flat-playlist",
            "--skip-download",
            "--extractor-args",
            "youtubetab:approximate_date",
            "--playlist-start",
            "1",
            "--playlist-end",
            "2",
            "ytsearch2:cats"
        };

        var result = await host.RunAsync("yt-dlp", args, TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardOutput));
        Assert.Contains("cats", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_WithConcurrentRequests_ExecutesBothSuccessfully()
    {
        await using var host = new YtDlpProcessHost();
        var task1 = host.RunAsync("yt-dlp", ["--version"], TimeSpan.FromSeconds(10), CancellationToken.None);
        var task2 = host.RunAsync("yt-dlp", ["--version"], TimeSpan.FromSeconds(10), CancellationToken.None);

        var results = await Task.WhenAll(task1, task2);

        Assert.Equal(0, results[0].ExitCode);
        Assert.Equal(0, results[1].ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(results[0].StandardOutput));
        Assert.False(string.IsNullOrWhiteSpace(results[1].StandardOutput));
    }

    [Fact]
    public async Task RestartAsync_RestartsProcessAndMaintainsState()
    {
        await using var host = new YtDlpProcessHost();
        await host.EnsureStartedAsync("yt-dlp", CancellationToken.None);
        var initialVersion = host.Version;
        Assert.True(host.IsRunning);

        await host.RestartAsync("yt-dlp", CancellationToken.None);
        Assert.True(host.IsRunning);
        Assert.Equal(initialVersion, host.Version);
    }

    [Fact]
    public async Task DisposeAsync_ShutsDownHelperProcess()
    {
        var host = new YtDlpProcessHost();
        await host.EnsureStartedAsync("yt-dlp", CancellationToken.None);
        Assert.True(host.IsRunning);

        await host.DisposeAsync();
        Assert.False(host.IsRunning);
    }
}
