using SilverScreen.Account.Auth;

namespace SilverScreen.Tests.Account.Auth;

public sealed class WebLoginCaptureCoordinatorTests
{
    [Fact]
    public async Task RequestCapture_DebouncesMultipleRequests()
    {
        var readCount = 0;
        var persistCount = 0;
        var persistedCount = 0;
        var tcs = new TaskCompletionSource<bool>();

        using var coordinator = new WebLoginCaptureCoordinator(
            readReadyCookies: () =>
            {
                Interlocked.Increment(ref readCount);
                return Task.FromResult<string?>("valid-cookie");
            },
            persist: _ =>
            {
                Interlocked.Increment(ref persistCount);
                return true;
            },
            persisted: () =>
            {
                Interlocked.Increment(ref persistedCount);
                tcs.TrySetResult(true);
            },
            readFailed: _ => { },
            persistenceFailed: () => { },
            debounceDelay: TimeSpan.FromMilliseconds(50));

        // Fire multiple rapid capture requests
        coordinator.RequestCapture();
        coordinator.RequestCapture();
        coordinator.RequestCapture();

        await Task.WhenAny(tcs.Task, Task.Delay(2000));

        Assert.True(tcs.Task.IsCompletedSuccessfully);
        Assert.Equal(1, readCount);
        Assert.Equal(1, persistCount);
        Assert.Equal(1, persistedCount);
    }

    [Fact]
    public async Task RequestCapture_WhenReadReturnsNull_DoesNotPersist()
    {
        var readCount = 0;
        var persistCount = 0;
        var firstReadTcs = new TaskCompletionSource<bool>();
        var persistedTcs = new TaskCompletionSource<bool>();

        using var coordinator = new WebLoginCaptureCoordinator(
            readReadyCookies: () =>
            {
                var count = Interlocked.Increment(ref readCount);
                if (count == 1)
                {
                    firstReadTcs.TrySetResult(true);
                    return Task.FromResult<string?>(null); // Incomplete cookies (e.g. solitary SAPISID)
                }

                return Task.FromResult<string?>("valid-cookie");
            },
            persist: _ =>
            {
                Interlocked.Increment(ref persistCount);
                persistedTcs.TrySetResult(true);
                return true;
            },
            persisted: () => { },
            readFailed: _ => { },
            persistenceFailed: () => { },
            debounceDelay: TimeSpan.FromMilliseconds(20));

        coordinator.RequestCapture();
        await Task.WhenAny(firstReadTcs.Task, Task.Delay(2000));

        Assert.True(firstReadTcs.Task.IsCompletedSuccessfully);
        Assert.Equal(1, readCount);
        Assert.Equal(0, persistCount);

        // Second capture arrives later with complete cookies
        coordinator.RequestCapture();
        await Task.WhenAny(persistedTcs.Task, Task.Delay(2000));

        Assert.True(persistedTcs.Task.IsCompletedSuccessfully);
        Assert.Equal(2, readCount);
        Assert.Equal(1, persistCount);
    }

    [Fact]
    public async Task StopAsync_CancelsPendingDebouncedCapture()
    {
        var readCount = 0;

        using var coordinator = new WebLoginCaptureCoordinator(
            readReadyCookies: () =>
            {
                Interlocked.Increment(ref readCount);
                return Task.FromResult<string?>("valid-cookie");
            },
            persist: _ => true,
            persisted: () => { },
            readFailed: _ => { },
            persistenceFailed: () => { },
            debounceDelay: TimeSpan.FromMilliseconds(200));

        coordinator.RequestCapture();
        await coordinator.StopAsync();
        await Task.Delay(300);

        Assert.Equal(0, readCount);
    }
}
