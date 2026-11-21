using Microsoft.Extensions.DependencyInjection;
using SilverScreen.Shell;

namespace SilverScreen.Tests.Shell;

public sealed class NotificationServiceTests
{
    [Fact]
    public void NotificationService_InitialState_HasNoOverlayAttached()
    {
        var service = new NotificationService();
        Assert.False(service.IsOverlayAttached);
    }

    [Fact]
    public void AttachToastOverlay_WithNull_ThrowsArgumentNullException()
    {
        var service = new NotificationService();
        Assert.Throws<ArgumentNullException>(() => service.AttachToastOverlay(null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ShowToast_NullOrWhitespace_DoesNotThrowEvenWithoutOverlay(string? invalidMessage)
    {
        var service = new NotificationService();
        service.ShowToast(invalidMessage!);
    }

    [Fact]
    public void ShowToast_WithoutOverlay_DropsSilentlyWithoutThrowing()
    {
        var service = new NotificationService();
        service.ShowToast("A notification message");
    }

    [Fact]
    public void DetachToastOverlay_ClearsOverlay()
    {
        var service = new NotificationService();
        service.DetachToastOverlay();
        Assert.False(service.IsOverlayAttached);
    }

    [Fact]
    public void DependencyInjection_RegistersNavigationAndNotificationSingletons()
    {
        var services = new ServiceCollection();
        var config = new ApplicationConfiguration();
        services.AddSilverScreenServices(config);

        using var provider = services.BuildServiceProvider();

        var navInterface = provider.GetRequiredService<INavigationService>();
        var navConcrete = provider.GetRequiredService<NavigationService>();
        Assert.NotNull(navInterface);
        Assert.Same(navInterface, navConcrete);

        var notifyInterface = provider.GetRequiredService<INotificationService>();
        var notifyConcrete = provider.GetRequiredService<NotificationService>();
        Assert.NotNull(notifyInterface);
        Assert.Same(notifyInterface, notifyConcrete);
    }
}
