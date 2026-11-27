using Adw;
using Gdk;
using Gio;
using GLib;
using GObject;
using Gtk;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SilverScreen.Account.Profile;
using SilverScreen.Browsing.Search;
using SilverScreen.Core.Common;
using SilverScreen.Core.Downloads;
using SilverScreen.Core.Player;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Common;
using Application = Adw.Application;
using Functions = GLib.Functions;
using Task = System.Threading.Tasks.Task;
using Window = Gtk.Window;

namespace SilverScreen.Shell;

[Subclass<Application>]
public partial class App
{
    private static CssProvider? _styles;
    private static CssProvider? _playerStyles;
    private readonly ActivationWindowGuard<Window> _windowGuard = new();
    private AccountServices? _accountServices;
    private string? _initialQuery;
    private EventHandler<AppPreferences>? _preferencesChangedHandler;
    private ApplicationServiceLifetime? _serviceLifetime;
    private IServiceProvider? _serviceProvider;

    public void SetInitialQuery(string? query)
    {
        _initialQuery = query;
    }

    partial void Initialize()
    {
        ApplicationId = ApplicationMetadata.ApplicationId;
        Flags = ApplicationFlags.FlagsNone;
        OnActivate += Activate;
        OnShutdown += (_, _) =>
        {
            _styles?.Dispose();
            _styles = null;
            _playerStyles?.Dispose();
            _playerStyles = null;
            if (_preferencesChangedHandler is not null && _accountServices is not null)
            {
                _accountServices.Preferences.PreferencesChanged -= _preferencesChangedHandler;
                _preferencesChangedHandler = null;
            }

            _accountServices = null;
            _serviceLifetime?.ApplicationStopped();
        };
    }

    public void UseServices(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        if (Interlocked.CompareExchange(ref _serviceProvider, serviceProvider, null) is not null)
            throw new InvalidOperationException("Application services have already been configured.");

        _serviceLifetime = new ApplicationServiceLifetime(() =>
        {
            if (_preferencesChangedHandler is not null && _accountServices is not null)
            {
                _accountServices.Preferences.PreferencesChanged -= _preferencesChangedHandler;
                _preferencesChangedHandler = null;
            }

            _accountServices = null;
            var provider = Interlocked.Exchange(ref _serviceProvider, null);
            (provider as IDisposable)?.Dispose();
        });
    }

    private void Activate(Gio.Application sender, EventArgs args)
    {
        if (_serviceProvider is null)
            throw new InvalidOperationException("Application services have not been configured.");

        if (_windowGuard.TryPresentExisting(window => window.Present()))
            return;

        InstallStyles();
        var account = _serviceProvider.GetRequiredService<AccountServices>();
        _accountServices = account;
        var initialPreferences = account.Preferences.GetPreferences();
        ApplyTheme(initialPreferences.ThemeMode);
        if (_preferencesChangedHandler is null)
        {
            _preferencesChangedHandler = (_, prefs) => ApplyTheme(prefs.ThemeMode);
            account.Preferences.PreferencesChanged += _preferencesChangedHandler;
        }

        Window? createdWindow = null;
        var mainWindowWrapper = new MainWindow(
            _serviceProvider.GetRequiredService<BrowsingServices>(),
            account,
            _serviceProvider.GetRequiredService<Lazy<IPlaybackService>>(),
            _serviceProvider.GetRequiredService<Lazy<PlayerDependencies>>(),
            _serviceProvider.GetRequiredService<RuntimeDependencyDiagnostics>(),
            _serviceProvider.GetRequiredService<INavigationService>(),
            _serviceProvider.GetRequiredService<INotificationService>(),
            _serviceProvider.GetRequiredService<SearchViewModel>(),
            _serviceProvider.GetRequiredService<AccountViewModel>(),
            _serviceProvider.GetRequiredService<IShellRefreshCoordinator>(),
            () =>
            {
                if (createdWindow is not null)
                    _windowGuard.Clear(createdWindow);
                _serviceLifetime!.WindowClosed();
            },
            _initialQuery,
            _serviceProvider.GetRequiredService<ILibraryService>(),
            _serviceProvider.GetRequiredService<IDownloadService>());
        _initialQuery = null;
        var mainWindow = mainWindowWrapper.Widget;
        createdWindow = mainWindow;
        _windowGuard.Set(mainWindow);
        _serviceLifetime!.WindowOpened();
        mainWindow.Application = this;
        AddWindow(mainWindow);
        mainWindow.Present();

        Task.Run(() =>
        {
            ApplicationComposition.SweepStaleTemporaryFiles();
            Program.LogExternalDependencyVersions(initialPreferences);
        }).FireAndForget(Log.Logger);
    }

    private static void ApplyTheme(ThemeMode theme)
    {
        if (Display.GetDefault() is null)
        {
            Log.Debug("Skipping theme apply: no display available");
            return;
        }

        if (MainContext.Default().IsOwner())
        {
            ApplyThemeCore(theme);
            return;
        }

        Functions.IdleAdd(0, () =>
        {
            ApplyThemeCore(theme);
            return false;
        });
    }

    private static void ApplyThemeCore(ThemeMode theme)
    {
        var styleManager = StyleManager.GetDefault();
        styleManager.ColorScheme = theme switch
        {
            ThemeMode.Light => ColorScheme.PreferLight,
            ThemeMode.Dark => ColorScheme.PreferDark,
            _ => ColorScheme.Default
        };
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

        // KDE Plasma sets gtk-primary-button-warps-slider=false in gtk-4.0/settings.ini,
        // which prevents clicking the timeline trough to seek directly.
        Gtk.Settings.GetForDisplay(display).GtkPrimaryButtonWarpsSlider = true;

        _styles = CssProvider.New();
        _styles.LoadFromResource("/SilverScreen/Styles/main.css");

        StyleContext.AddProviderForDisplay(display, _styles, 600);
    }

    internal static void EnsurePlayerStylesInstalled()
    {
        if (_playerStyles is not null)
            return;

        var display = Display.GetDefault();
        if (display is null)
        {
            Log.Debug("Skipping player style install: no display available");
            return;
        }

        _playerStyles = CssProvider.New();
        _playerStyles.LoadFromResource("/SilverScreen/Styles/player.css");

        StyleContext.AddProviderForDisplay(display, _playerStyles, 600);
    }
}