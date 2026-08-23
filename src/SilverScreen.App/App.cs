using Adw;
using Gdk;
using Gio;
using GObject;
using Gtk;
using Microsoft.Extensions.DependencyInjection;
using SilverScreen.Views.Shell;
using Application = Adw.Application;
using Functions = GLib.Functions;

namespace SilverScreen;

[Subclass<Application>]
public partial class App
{
    private static CssProvider? _styles;
    private IServiceProvider? _serviceProvider;
    private bool _servicesDisposed;

    partial void Initialize()
    {
        ApplicationId = ApplicationMetadata.ApplicationId;
        Flags = ApplicationFlags.FlagsNone;
        OnActivate += Activate;
    }

    public void UseServices(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        if (Interlocked.CompareExchange(ref _serviceProvider, serviceProvider, null) is not null)
            throw new InvalidOperationException("Application services have already been configured.");
    }

    private void Activate(Gio.Application sender, EventArgs args)
    {
        var services = _serviceProvider?.GetRequiredService<ApplicationServices>()
                       ?? throw new InvalidOperationException("Application services have not been configured.");

        InstallStyles();
        ApplyTheme(services.Preferences.GetPreferences().Theme);
        services.Preferences.PreferencesChanged += (_, prefs) => ApplyTheme(prefs.Theme);

        var mainWindowWrapper = new MainWindow(services, DisposeServices);
        var mainWindow = mainWindowWrapper.Widget;
        mainWindow.Application = this;
        AddWindow(mainWindow);
        mainWindow.Present();
    }

    private static void ApplyTheme(string theme)
    {
        Functions.IdleAdd(0, () =>
        {
            var styleManager = StyleManager.GetDefault();
            styleManager.ColorScheme = theme switch
            {
                "Light" => ColorScheme.PreferLight,
                "Dark" => ColorScheme.PreferDark,
                _ => ColorScheme.Default
            };
            return false;
        });
    }

    private static void InstallStyles()
    {
        if (_styles is not null)
            return;

        if (Display.GetDefault() is { } display)
            IconTheme.GetForDisplay(display).AddResourcePath("/SilverScreen/Assets");

        _styles = CssProvider.New();
        _styles.LoadFromResource("/SilverScreen/Styles/main.css");

        StyleContext.AddProviderForDisplay(Display.GetDefault()!, _styles, 600);
    }

    private void DisposeServices()
    {
        if (_servicesDisposed) return;

        _servicesDisposed = true;
        (_serviceProvider as IDisposable)?.Dispose();
    }
}