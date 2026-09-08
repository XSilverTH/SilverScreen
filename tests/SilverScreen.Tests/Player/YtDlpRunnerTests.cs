using System.Diagnostics;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.YouTube;
using YoutubeAPI;

namespace SilverScreen.Tests.Player;

public sealed class YtDlpRunnerTests
{
    [Fact]
    public async Task RunAsync_NullStartInfo_ThrowsArgumentNullException()
    {
        var runner = new YtDlpRunner();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            runner.RunAsync(null!, TimeSpan.FromSeconds(5), CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_NonZeroExit_CapturesStdoutAndStderr()
    {
        var runner = new YtDlpRunner();
        var startInfo = new ProcessStartInfo
        {
            FileName = "sh",
            ArgumentList = { "-c", "echo 'out text'; echo 'err diagnostic' >&2; exit 2" }
        };

        var result = await runner.RunAsync(startInfo, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("out text", result.StandardOutput);
        Assert.Contains("err diagnostic", result.StandardError);
    }

    [Fact]
    public async Task RunAsync_ExitCodeZero_CapturesOutputAndDiagnostics()
    {
        var runner = new YtDlpRunner();
        var startInfo = new ProcessStartInfo
        {
            FileName = "sh",
            ArgumentList = { "-c", "echo 'hello world'; echo 'warning diagnostic' >&2; exit 0" }
        };

        var result = await runner.RunAsync(startInfo, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello world", result.StandardOutput);
        Assert.Contains("warning diagnostic", result.StandardError);
    }

    [Fact]
    public async Task RunAsync_EnsuresRedirectStandardOutputAndError()
    {
        var runner = new YtDlpRunner();
        var startInfo = new ProcessStartInfo
        {
            FileName = "sh",
            ArgumentList = { "-c", "echo 'captured'" },
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = true
        };

        var result = await runner.RunAsync(startInfo, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("captured", result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_WhenTimesOut_ThrowsTimeoutExceptionAndKillsProcess()
    {
        var runner = new YtDlpRunner();
        var startInfo = new ProcessStartInfo
        {
            FileName = "sh",
            ArgumentList = { "-c", "sleep 10" }
        };

        await Assert.ThrowsAsync<TimeoutException>(() =>
            runner.RunAsync(startInfo, TimeSpan.FromMilliseconds(100), CancellationToken.None));
    }
}

public sealed class YtDlpMediaResolverTests
{
    [Fact]
    public async Task ResolveMediaAsync_InvalidVideoId_ReturnsFailure()
    {
        using var resolver = CreateResolver(new FakeYtDlpRunner());

        var result = await resolver.ResolveMediaAsync("invalid id with spaces");

        Assert.False(result.IsSuccess);
        Assert.Equal("Media is unavailable for this video.", result.StatusMessage);
    }

    [Fact]
    public async Task ResolveMediaAsync_NonZeroExitWithStderr_IncludesStderrDiagnosticsInErrorMessage()
    {
        var runner = new FakeYtDlpRunner
        {
            Result = new ProcessResult(1, "", "ERROR: [youtube] dQw4w9WgXcQ: Sign in to confirm you're not a bot")
        };
        using var resolver = CreateResolver(runner);

        var result = await resolver.ResolveMediaAsync("dQw4w9WgXcQ");

        Assert.False(result.IsSuccess);
        Assert.Contains("error code 1: ERROR: [youtube] dQw4w9WgXcQ: Sign in to confirm you're not a bot", result.StatusMessage);
    }

    [Fact]
    public async Task ResolveMediaAsync_NonZeroExitWithoutStderr_ReturnsErrorCodeMessage()
    {
        var runner = new FakeYtDlpRunner
        {
            Result = new ProcessResult(1, "", "")
        };
        using var resolver = CreateResolver(runner);

        var result = await resolver.ResolveMediaAsync("dQw4w9WgXcQ");

        Assert.False(result.IsSuccess);
        Assert.Contains("error code 1.", result.StatusMessage);
    }

    [Fact]
    public async Task ResolveMediaAsync_EmptyOutput_ReturnsNoOutputError()
    {
        var runner = new FakeYtDlpRunner
        {
            Result = new ProcessResult(0, "", "warning: format not found")
        };
        using var resolver = CreateResolver(runner);

        var result = await resolver.ResolveMediaAsync("dQw4w9WgXcQ");

        Assert.False(result.IsSuccess);
        Assert.Contains("the process returned no output", result.StatusMessage);
    }

    [Fact]
    public async Task ResolveMediaAsync_Timeout_ReturnsTimedOutGuidance()
    {
        var runner = new FakeYtDlpRunner
        {
            ExceptionToThrow = new TimeoutException("Timed out")
        };
        using var resolver = CreateResolver(runner);

        var result = await resolver.ResolveMediaAsync("dQw4w9WgXcQ");

        Assert.False(result.IsSuccess);
        Assert.Equal(RuntimeDependencyGuidance.YtDlpTimedOut, result.StatusMessage);
    }

    private static YtDlpMediaResolver CreateResolver(IYtDlpRunner runner)
    {
        return new YtDlpMediaResolver(
            new FakeCookieFileProvider(),
            new FakePreferencesService(),
            runner,
            new FakeYouTubeClientProvider());
    }

    private sealed class FakeYtDlpRunner : IYtDlpRunner
    {
        public ProcessResult Result { get; set; } = new(0, "{}", "");
        public Exception? ExceptionToThrow { get; set; }

        public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;

            return Task.FromResult(Result);
        }
    }

    private sealed class FakeCookieFileProvider : ICookieFileProvider
    {
        public CookieFileLease? CreateCookieFile() => null;
    }

    private sealed class FakePreferencesService : IPreferencesService
    {
        public AppPreferences GetPreferences() => new() { YtDlpExecutablePath = "yt-dlp", Quality = VideoQuality.P1080 };
        public void SavePreferences(AppPreferences preferences) { }
#pragma warning disable CS0067
        public event EventHandler<AppPreferences>? PreferencesChanged;
#pragma warning restore CS0067
    }

    private sealed class FakeYouTubeClientProvider : IYouTubeClientProvider
    {
        public YouTubeClient GetClient() => throw new NotImplementedException();
    }
}
