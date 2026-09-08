using Adw;
using Gdk;
using Gio;
using GObject;
using Gtk;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SilverScreen.Core.Player;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Player;
using SilverScreen.Core.Preferences;
using Application = Adw.Application;
using Functions = GLib.Functions;
using Window = Gtk.Window;
namespace SilverScreen.Shell;

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
        OnShutdown += (_, _) =>
        {
            _styles?.Dispose();
            _styles = null;
            DisposeServices();
        };
    }

    public void UseServices(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        if (Interlocked.CompareExchange(ref _serviceProvider, serviceProvider, null) is not null)
            throw new InvalidOperationException("Application services have already been configured.");
    }

    private void Activate(Gio.Application sender, EventArgs args)
    {
        if (_serviceProvider is null)
            throw new InvalidOperationException("Application services have not been configured.");

        InstallStyles();
        var account = _serviceProvider.GetRequiredService<AccountServices>();
        ApplyTheme(account.Preferences.GetPreferences().ThemeMode);
        account.Preferences.PreferencesChanged += (_, prefs) => ApplyTheme(prefs.ThemeMode);

        var mainWindowWrapper = new MainWindow(
            _serviceProvider.GetRequiredService<BrowsingServices>(),
            account,
            _serviceProvider.GetRequiredService<IPlaybackService>(),
            _serviceProvider.GetRequiredService<PlayerDependencies>(),
            _serviceProvider.GetRequiredService<RuntimeDependencyDiagnostics>(),
            DisposeServices);
        var mainWindow = mainWindowWrapper.Widget;
        mainWindow.Application = this;
        AddWindow(mainWindow);
        mainWindow.Present();
    }

    private static void ApplyTheme(ThemeMode theme)
    {
        if (Display.GetDefault() is null)
        {
            Log.Debug("Skipping theme apply: no display available");
            return;
        }

        Functions.IdleAdd(0, () =>
        {
            var styleManager = StyleManager.GetDefault();
            styleManager.ColorScheme = theme switch
            {
                ThemeMode.Light => ColorScheme.PreferLight,
                ThemeMode.Dark => ColorScheme.PreferDark,
                _ => ColorScheme.Default
            };
            return false;
        });
    }

    private static void InstallStyles()
    {
        if (_styles is not null)
            return;

        var display = Display.GetDefault();
        if (display is null)
        {
            Log.Debug("Skipping style install: no display available");
            return;
        }

        IconTheme.GetForDisplay(display).AddResourcePath("/SilverScreen/Assets");
        Window.SetDefaultIconName(ApplicationMetadata.IconName);

        _styles = CssProvider.New();
        _styles.LoadFromResource("/SilverScreen/Styles/main.css");

        StyleContext.AddProviderForDisplay(display, _styles, 600);
    }

    private void DisposeServices()
    {
        if (_servicesDisposed) return;

        _servicesDisposed = true;
        (_serviceProvider as IDisposable)?.Dispose();
    }
}