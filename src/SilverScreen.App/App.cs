using SilverScreen.Views.Shell;

namespace SilverScreen;

public class App : Adw.Application
{
    private readonly ApplicationServices _services = new();
    private bool _servicesDisposed;

    public App()
    {
        ApplicationId = "io.github.silverscreen.SilverScreen";
        Flags = Gio.ApplicationFlags.FlagsNone;
        OnActivate += Activate;
    }

    private void Activate(Gio.Application sender, EventArgs args)
    {
        var mainWindowWrapper = new MainWindow(_services, DisposeServices);
        var mainWindow = mainWindowWrapper.Widget;
        mainWindow.Application = this;
        AddWindow(mainWindow);
        mainWindow.Present();
    }

    private void DisposeServices()
    {
        if (_servicesDisposed)
        {
            return;
        }

        _servicesDisposed = true;
        _services.Dispose();
    }
}