using Adw;
using Gdk;
using Gio;
using GObject;
using Gtk;
using Microsoft.Extensions.DependencyInjection;
using Application = Adw.Application;
using Functions = GLib.Functions;

namespace SilverScreen.Shell;

[Subclass<Application>]
public partial class App
{
    private static CssProvider? _styles;
    private IServiceProvider? _serviceProvider;
    private MainWindow? _mainWindow;
    private bool _servicesDisposed;
    private bool _themeSubscribed;
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
        if (!_themeSubscribed)
        {
            services.Preferences.PreferencesChanged += (_, prefs) => ApplyTheme(prefs.Theme);
            _themeSubscribed = true;
        }
        ApplyTheme(services.Preferences.GetPreferences().Theme);

        if (_mainWindow is not null)
        {
            _mainWindow.Widget.Present();
            return;
        }

        _mainWindow = new MainWindow(services, OnMainWindowClosed);
        _mainWindow.Widget.Application = this;
        AddWindow(_mainWindow.Widget);
        _mainWindow.Widget.Present();
    }

    private void OnMainWindowClosed()
    {
        _mainWindow = null;
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