using SilverScreen.Shell;

namespace SilverScreen.Tests.Shell;

public sealed class ShellRefreshCoordinatorTests
{
    [Fact]
    public void IsLoading_WithoutHandler_ReturnsFalse()
    {
        var coordinator = new ShellRefreshCoordinator();

        Assert.False(coordinator.IsLoading);
    }

    [Fact]
    public void IsLoading_WithHandler_ReturnsHandlerResult()
    {
        var coordinator = new ShellRefreshCoordinator();
        var loading = false;

        coordinator.RegisterHandler(() => Task.CompletedTask, () => loading);

        Assert.False(coordinator.IsLoading);

        loading = true;
        Assert.True(coordinator.IsLoading);
    }

    [Fact]
    public async Task RefreshActivePageAsync_InvokesRegisteredHandler()
    {
        var coordinator = new ShellRefreshCoordinator();
        var invoked = false;

        coordinator.RegisterHandler(() =>
        {
            invoked = true;
            return Task.CompletedTask;
        }, () => false);

        await coordinator.RefreshActivePageAsync();

        Assert.True(invoked);
    }

    [Fact]
    public void NotifyLoadingChanged_FiresEventWithCurrentLoadingState()
    {
        var coordinator = new ShellRefreshCoordinator();
        var loading = false;
        var receivedStates = new List<bool>();

        coordinator.RegisterHandler(() => Task.CompletedTask, () => loading);
        coordinator.LoadingChanged += (_, state) => receivedStates.Add(state);

        loading = true;
        coordinator.NotifyLoadingChanged();

        loading = false;
        coordinator.NotifyLoadingChanged();

        Assert.Equal([true, false], receivedStates);
    }
}
