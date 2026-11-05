using System.Diagnostics;
using SilverScreen.Account.Auth;
using SilverScreen.Core.Account.Session;
using SilverScreen.Infrastructure.Account.Session;

namespace SilverScreen.Tests.Account.Auth;

public sealed class NativeAuthServiceTests
{
    private const string SampleNetscapeCookies =
        "# Netscape HTTP Cookie File\n" +
        ".youtube.com\tTRUE\t/\tTRUE\t2147483647\tSAPISID\tsapisid_value\n" +
        ".youtube.com\tTRUE\t/\tTRUE\t2147483647\tSID\tsid_value\n";

    [Fact]
    public async Task StartAuthAsync_WhenWorkerSucceeds_PersistsCookies()
    {
        using var sessionService = new InMemorySessionService();
        using var authService = new NativeAuthService(
            sessionService,
            _ =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add($"cat << 'EOF'\n{SampleNetscapeCookies}EOF");
                return Process.Start(psi);
            },
            () => "/bin/sh");

        var result = await authService.StartAuthAsync();

        Assert.True(result);
        var cookies = sessionService.GetManualSessionCookies();
        Assert.NotNull(cookies);
        Assert.Equal(SampleNetscapeCookies.Trim(), cookies.Content);
        Assert.Equal(SessionCookieFormat.NetscapeCookiesText, cookies.Format);
    }

    [Fact]
    public async Task StartAuthAsync_WhenWorkerExitsNonZero_ReturnsFalseWithoutThrowing()
    {
        using var sessionService = new InMemorySessionService();
        using var authService = new NativeAuthService(
            sessionService,
            _ => Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = "-c 'exit 1'",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }),
            () => "/bin/sh");

        var result = await authService.StartAuthAsync();

        Assert.False(result);
        Assert.Null(sessionService.GetManualSessionCookies());
    }

    [Fact]
    public async Task StartAuthAsync_WhenWorkerWritesEmptyOutput_ReturnsFalse()
    {
        using var sessionService = new InMemorySessionService();
        using var authService = new NativeAuthService(
            sessionService,
            _ => Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = "-c 'exit 0'",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }),
            () => "/bin/sh");

        var result = await authService.StartAuthAsync();

        Assert.False(result);
        Assert.Null(sessionService.GetManualSessionCookies());
    }

    [Fact]
    public async Task StartAuthAsync_WhenAlreadyRunning_ReturnsFalse()
    {
        using var sessionService = new InMemorySessionService();
        using var authService = new NativeAuthService(
            sessionService,
            _ => Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = "-c 'sleep 0.5'",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }),
            () => "/bin/sh");

        var task1 = authService.StartAuthAsync();
        var task2 = authService.StartAuthAsync();

        var results = await Task.WhenAll(task1, task2);

        Assert.Contains(false, results);
    }

    [Fact]
    public async Task StartAuthAsync_WhenCancelled_ReturnsFalse()
    {
        using var sessionService = new InMemorySessionService();
        using var authService = new NativeAuthService(
            sessionService,
            _ => Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = "-c 'sleep 5'",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }),
            () => "/bin/sh");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var result = await authService.StartAuthAsync(cts.Token);

        Assert.False(result);
    }

    [Fact]
    public async Task Dispose_AbortsInFlightProcess()
    {
        using var sessionService = new InMemorySessionService();
        var authService = new NativeAuthService(
            sessionService,
            _ => Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = "-c 'sleep 5'",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }),
            () => "/bin/sh");

        var task = authService.StartAuthAsync();
        await Task.Delay(50);
        authService.Dispose();

        var result = await task;
        Assert.False(result);
    }
}