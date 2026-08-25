using System.Diagnostics;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests.YouTube;

public sealed class WarmYtDlpRunnerTests
{
    [Fact]
    public async Task RunAsync_WhenHostSucceeds_ReturnsHostResult()
    {
        var preferences = new TestPreferences("yt-dlp");
        var fakeHost = new FakeProcessHost
        {
            RunHandler = (path, args, timeout, ct) =>
                Task.FromResult(new ProcessResult(0, "{\"title\":\"Success\"}", string.Empty))
        };
        var fakeFallback = new FakeRunner(_ => Task.FromResult(new ProcessResult(1, string.Empty, "Fallback used")));

        using var runner = new WarmYtDlpRunner(preferences, fakeHost, fakeFallback);

        var startInfo = new ProcessStartInfo("yt-dlp");
        startInfo.ArgumentList.Add("--dump-single-json");

        var result = await runner.RunAsync(startInfo, TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Success", result.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(0, fakeFallback.RunCallCount);
    }

    [Fact]
    public async Task RunAsync_WhenHostThrows_FallsBackToDirectRunner()
    {
        var preferences = new TestPreferences("yt-dlp");
        var fakeHost = new FakeProcessHost
        {
            RunHandler = (_, _, _, _) => throw new InvalidOperationException("Host is dead")
        };
        var fakeFallback = new FakeRunner(info =>
        {
            Assert.Contains("--dump-single-json", info.ArgumentList);
            return Task.FromResult(new ProcessResult(0, "{\"title\":\"Fallback Success\"}", string.Empty));
        });

        using var runner = new WarmYtDlpRunner(preferences, fakeHost, fakeFallback);

        var startInfo = new ProcessStartInfo("yt-dlp");
        startInfo.ArgumentList.Add("--dump-single-json");

        var result = await runner.RunAsync(startInfo, TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Fallback Success", result.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(1, fakeFallback.RunCallCount);
    }

    [Fact]
    public async Task RunAsync_WhenCancellationTokenCanceled_ThrowsOperationCanceledException()
    {
        var preferences = new TestPreferences("yt-dlp");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var fakeHost = new FakeProcessHost
        {
            RunHandler = (_, _, _, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new ProcessResult(0, "{}", string.Empty));
            }
        };
        var fakeFallback = new FakeRunner(_ => Task.FromResult(new ProcessResult(0, "{}", string.Empty)));

        using var runner = new WarmYtDlpRunner(preferences, fakeHost, fakeFallback);

        var startInfo = new ProcessStartInfo("yt-dlp");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await runner.RunAsync(startInfo, TimeSpan.FromSeconds(10), cts.Token));
    }

    [Fact]
    public async Task RunAsync_WhenTimeoutOccurs_ThrowsTimeoutException()
    {
        var preferences = new TestPreferences("yt-dlp");
        var fakeHost = new FakeProcessHost
        {
            RunHandler = (_, _, _, _) => throw new TimeoutException("Timed out")
        };
        var fakeFallback = new FakeRunner(_ => Task.FromResult(new ProcessResult(0, "{}", string.Empty)));

        using var runner = new WarmYtDlpRunner(preferences, fakeHost, fakeFallback);

        var startInfo = new ProcessStartInfo("yt-dlp");
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await runner.RunAsync(startInfo, TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    [Fact]
    public void PreferencesChanged_TriggersProcessHostRestart()
    {
        var preferences = new TestPreferences("yt-dlp");
        var fakeHost = new FakeProcessHost();
        var fakeFallback = new FakeRunner(_ => Task.FromResult(new ProcessResult(0, "{}", string.Empty)));

        using var runner = new WarmYtDlpRunner(preferences, fakeHost, fakeFallback);

        preferences.SavePreferences(new AppPreferences { YtDlpExecutablePath = "/custom/path/yt-dlp" });

        Assert.Contains("/custom/path/yt-dlp", fakeHost.RestartedPaths);
    }

    [Fact]
    public void Dispose_DisposesHostAndFallback()
    {
        var preferences = new TestPreferences("yt-dlp");
        var fakeHost = new FakeProcessHost();
        var fakeFallback = new FakeRunner(_ => Task.FromResult(new ProcessResult(0, "{}", string.Empty)));

        var runner = new WarmYtDlpRunner(preferences, fakeHost, fakeFallback);
        runner.Dispose();

        Assert.True(fakeHost.IsDisposed);
        Assert.True(fakeFallback.IsDisposed);
    }

    [Fact]
    public async Task RunAsync_WithCookieFilePath_PassesCookiesToHost()
    {
        var preferences = new TestPreferences("yt-dlp");
        IReadOnlyList<string>? capturedArgs = null;
        var fakeHost = new FakeProcessHost
        {
            RunHandler = (path, args, timeout, ct) =>
            {
                capturedArgs = args;
                return Task.FromResult(new ProcessResult(0, "{}", string.Empty));
            }
        };
        var fakeFallback = new FakeRunner(_ => Task.FromResult(new ProcessResult(1, string.Empty, string.Empty)));

        using var runner = new WarmYtDlpRunner(preferences, fakeHost, fakeFallback);

        var startInfo = YtDlpCommandBuilder.BuildSubscriptions("yt-dlp", 1, 10, "/tmp/cookies.txt");
        var result = await runner.RunAsync(startInfo, TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(capturedArgs);
        Assert.Contains("--cookies", capturedArgs);
        Assert.Contains("/tmp/cookies.txt", capturedArgs);
    }

    [Fact]
    public async Task RunAsync_WithArgumentsString_ExtractsArgumentsProperly()
    {
        var preferences = new TestPreferences("yt-dlp");
        IReadOnlyList<string>? capturedArgs = null;
        var fakeHost = new FakeProcessHost
        {
            RunHandler = (path, args, timeout, ct) =>
            {
                capturedArgs = args;
                return Task.FromResult(new ProcessResult(0, "{}", string.Empty));
            }
        };
        var fakeFallback = new FakeRunner(_ => Task.FromResult(new ProcessResult(1, string.Empty, string.Empty)));

        using var runner = new WarmYtDlpRunner(preferences, fakeHost, fakeFallback);

        var startInfo = new ProcessStartInfo("yt-dlp")
        {
            Arguments = "--dump-single-json --flat-playlist ytsearch5:music"
        };

        var result = await runner.RunAsync(startInfo, TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(capturedArgs);
        Assert.Contains("--dump-single-json", capturedArgs);
        Assert.Contains("--flat-playlist", capturedArgs);
        Assert.Contains("ytsearch5:music", capturedArgs);
    }

    private sealed class FakeProcessHost : IYtDlpProcessHost
    {
        public bool IsRunning { get; set; } = true;
        public string? Version { get; set; } = "2026.08.19";
        public string? PythonVersion { get; set; } = "3.14.0";
        public bool IsDisposed { get; private set; }
        public List<string> RestartedPaths { get; } = [];

        public Func<string, IReadOnlyList<string>, TimeSpan, CancellationToken, Task<ProcessResult>> RunHandler { get; set; } =
            (_, _, _, _) => Task.FromResult(new ProcessResult(0, "{}", string.Empty));

        public Task<ProcessResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            return RunHandler(executablePath, arguments, timeout, cancellationToken);
        }

        public Task EnsureStartedAsync(string executablePath, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RestartAsync(string executablePath, CancellationToken cancellationToken = default)
        {
            RestartedPaths.Add(executablePath);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class FakeRunner(Func<ProcessStartInfo, Task<ProcessResult>> handler) : IYtDlpRunner, IDisposable
    {
        public int RunCallCount { get; private set; }
        public bool IsDisposed { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            RunCallCount++;
            return handler(startInfo);
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class TestPreferences(string ytDlpPath) : IPreferencesService
    {
        private AppPreferences _current = new() { YtDlpExecutablePath = ytDlpPath };

        public event EventHandler<AppPreferences>? PreferencesChanged;

        public AppPreferences GetPreferences() => _current;

        public void SavePreferences(AppPreferences preferences)
        {
            _current = preferences;
            PreferencesChanged?.Invoke(this, preferences);
        }
    }
}
