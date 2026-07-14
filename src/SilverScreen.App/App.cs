using Gtk;
using SilverScreen.Views.Shell;

namespace SilverScreen;

public class App : Adw.Application
{
    private static CssProvider? _styles;
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
        InstallStyles();
        ApplyTheme(_services.Preferences.GetPreferences().Theme);
        _services.Preferences.PreferencesChanged += (s, prefs) => ApplyTheme(prefs.Theme);

        var mainWindowWrapper = new MainWindow(_services, DisposeServices);
        var mainWindow = mainWindowWrapper.Widget;
        mainWindow.Application = this;
        AddWindow(mainWindow);
        mainWindow.Present();
    }

    private static void ApplyTheme(string theme)
    {
        GLib.Functions.IdleAdd(0, () =>
        {
            var styleManager = Adw.StyleManager.GetDefault();
            styleManager.ColorScheme = theme switch
            {
                "Light" => Adw.ColorScheme.PreferLight,
                "Dark" => Adw.ColorScheme.PreferDark,
                _ => Adw.ColorScheme.Default
            };
            return false;
        });
    }

    private static void InstallStyles()
    {
        if (_styles is not null)
            return;

        _styles = CssProvider.New();
        _styles.LoadFromString("""
            .video-card {
              background-color: @card_bg_color;
              border: 1px solid alpha(@borders, 0.72);
              border-radius: 16px;
              box-shadow: 0 2px 8px 1px alpha(@shade_color, 0.16);
            }

            .video-thumbnail {
              background-color: #1b1c20;
              border-radius: 15px 15px 0 0;
            }

            .video-thumbnail image {
              border-radius: 15px 15px 0 0;
            }

            .video-title {
              font-weight: 700;
            }

            .duration-pill {
              background-color: alpha(#000000, 0.78);
              border-radius: 7px;
              color: #ffffff;
              padding: 2px 6px;
            }
            """);

        StyleContext.AddProviderForDisplay(Gdk.Display.GetDefault()!, _styles, 600);
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