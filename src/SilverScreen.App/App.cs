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

                               .queue-panel {
                                 background-color: @window_bg_color;
                                 border-left: 1px solid @borders;
                               }

                               .queue-row {
                                 background-color: @card_bg_color;
                                 border: 1px solid @borders;
                                 border-radius: 12px;
                                 margin-top: 3px;
                                 margin-bottom: 3px;
                               }

                               .queue-thumbnail {
                                 background-color: #1b1c20;
                                 border-radius: 8px;
                               }

                               .queue-thumbnail image {
                                 border-radius: 8px;
                               }

                               .queue-row.queue-drop-before {
                                 border-top: 2px solid @accent_bg_color;
                               }

                               .queue-row.queue-drop-after {
                                 border-bottom: 2px solid @accent_bg_color;
                               }

                               .queue-count {
                                 background-color: @accent_bg_color;
                                 border-radius: 999px;
                                 color: @accent_fg_color;
                                 min-width: 1.5em;
                                 padding: 1px 5px;
                               }

                               .embedded-player,
                               .embedded-player-surface {
                                 background-color: #000000;
                               }

                               .player-headerbar,
                               .player-headerbar windowhandle,
                               .player-headerbar > windowhandle {
                                 background-color: transparent;
                                 background-image: none;
                                 box-shadow: none;
                               }

                               .player-controls {
                                 background-image: linear-gradient(to top, alpha(#000000, 0.88), alpha(#000000, 0.48), transparent);
                                 color: #ffffff;
                                 padding: 28px 0 0;
                               }

                               .player-title,
                               .player-subtitle,
                               .player-time {
                                 color: #ffffff;
                               }

                               .player-subtitle,
                               .player-time {
                                 color: alpha(#ffffff, 0.72);
                               }

                               .player-overlay-button,
                               .player-primary-control,
                               .player-headerbar button {
                                 background-color: transparent;
                                 color: #ffffff;
                                 transition: background-color 160ms ease-out;
                               }

                               .player-overlay-button:hover,
                               .player-primary-control:hover,
                               .player-headerbar button:hover {
                                 background-color: alpha(#ffffff, 0.16);
                               }

                               .player-center-controls {
                                 margin-bottom: 28px;
                               }

                               .player-seek-button {
                                 min-height: 40px;
                                 min-width: 40px;
                               }

                               .player-primary-control {
                                 min-height: 48px;
                                 min-width: 48px;
                               }

                               .player-controls scale trough {
                                 background-color: alpha(#ffffff, 0.28);
                                 min-height: 4px;
                               }

                               .player-controls scale highlight {
                                 background-color: #ffffff;
                               }

                               .player-controls scale slider {
                                 background-color: #ffffff;
                                 min-height: 18px;
                                 min-width: 18px;
                               }
                               """);

        StyleContext.AddProviderForDisplay(Display.GetDefault()!, _styles, 600);
    }

    private void DisposeServices()
    {
        if (_servicesDisposed) return;

        _servicesDisposed = true;
        (_serviceProvider as IDisposable)?.Dispose();
    }
}