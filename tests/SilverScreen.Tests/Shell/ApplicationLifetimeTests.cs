using SilverScreen.Shell;

namespace SilverScreen.Tests.Shell;

public sealed class ApplicationLifetimeTests
{
    [Fact]
    public void SecondActivation_PresentsExistingWindowInsteadOfCreatingAnother()
    {
        var guard = new ActivationWindowGuard<object>();
        var existingWindow = new object();
        var createdWindows = 0;
        var presentations = 0;

        bool Activate()
        {
            if (guard.TryPresentExisting(_ => presentations++))
                return false;

            createdWindows++;
            guard.Set(existingWindow);
            return true;
        }

        Assert.True(Activate());
        Assert.False(Activate());
        Assert.Equal(1, createdWindows);
        Assert.Equal(1, presentations);
    }

    [Fact]
    public void ServicesAreDisposedOnce_WhenLastWindowClosesOrApplicationStops()
    {
        var disposeCount = 0;
        var lifetime = new ApplicationServiceLifetime(() => disposeCount++);
        lifetime.WindowOpened();
        lifetime.WindowOpened();

        lifetime.WindowClosed();
        Assert.Equal(0, disposeCount);

        lifetime.WindowClosed();
        lifetime.ApplicationStopped();
        lifetime.ApplicationStopped();
        Assert.Equal(1, disposeCount);
    }
}
