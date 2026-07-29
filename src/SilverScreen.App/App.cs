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

                               .queue-indicator {
                                 min-height: 0;
                                 min-width: 0;
                                 padding: 0;
                               }

                               .queue-count {
                                 background-color: @accent_bg_color;
                                 border-radius: 999px;
                                 color: @accent_fg_color;
                                 font-size: 11px;
                                 min-height: 16px;
                                 min-width: 16px;
                                 padding: 0;
                               }

                               .account-sign-in-actions button {
                                 min-height: 40px;
                               }

                               .account-card {
                                 padding: 0;
                               }

                               .account-display-name {
                                 font-size: 1.05em;
                                 font-weight: 700;
                               }


                               .account-sign-out-button {
                                 background-color: alpha(@window_fg_color, 0.12);
                                 min-height: 40px;
                                 min-width: 40px;
                                 padding: 0;
                               }

                               .account-sign-out-button:hover {
                                 background-color: alpha(@window_fg_color, 0.18);
                               }


                               .embedded-player,
                               .embedded-player-surface {
                                 background-color: #000000;
                               }

                               .player-loading-indicator {
                                 color: #ffffff;
                                 margin-bottom: 28px;
                               }

                               .player-loading-subtitle {
                                 color: alpha(#ffffff, 0.72);
                               }

                               .player-headerbar,
                               .player-headerbar windowhandle,
                               .player-headerbar > windowhandle {
                                 background-color: transparent;
                                 background-image: none;
                                 box-shadow: none;
                               }

                               .player-headerbar,
                               .player-center-controls,
                               .player-controls {
                                 transition: opacity 250ms ease-out;
                               }

                               .player-chrome-hidden {
                                 opacity: 0;
                               }

                               .player-controls {
                                 background-image: linear-gradient(to top, alpha(#000000, 0.78) 0%, alpha(#000000, 0.42) 42%, alpha(#000000, 0) 100%);
                                 color: #ffffff;
                                 padding: 28px 32px 24px;
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
                               .player-headerbar button:hover {
                                 background-color: alpha(#ffffff, 0.16);
                               }

                               .player-reaction-button {
                                 color: #ffffff;
                                 min-height: 32px;
                                 padding: 4px 8px;
                               }

                               button.player-reaction-button:disabled {
                                 color: #ffffff;
                                 opacity: 1;
                               }

                               button.player-reaction-button:disabled label {
                                 color: #ffffff;
                                 opacity: 1;
                               }


                               .player-reaction-count {
                                 font-weight: 600;
                               }

                               .player-ryd-attribution {
                                 color: alpha(#ffffff, 0.72);
                                 font-size: smaller;
                                 padding: 4px;
                               }

                               .player-queue-controls {
                                 background-color: alpha(#ffffff, 0.14);
                                 border-radius: 14px;
                               }

                               button.player-queue-button {
                                 border-radius: 0;
                                 color: #ffffff;
                                 min-height: 28px;
                                 min-width: 28px;
                                 padding: 4px;
                               }

                               button.player-queue-button:hover {
                                 background-color: alpha(#ffffff, 0.16);
                               }

                               .player-queue-separator {
                                 background-color: alpha(#ffffff, 0.2);
                                 margin-bottom: 6px;
                                 margin-top: 6px;
                                 min-width: 1px;
                               }

                               .player-center-controls {
                                 margin-bottom: 28px;
                               }

                               .player-seek-button {
                                 min-height: 40px;
                                 min-width: 40px;
                               }

                               .player-primary-control {
                                 min-height: 72px;
                                 min-width: 72px;
                                 -gtk-icon-size: 64px;
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

                               button.player-chapter-marker,
                               button.player-chapter-marker:hover,
                               button.player-chapter-marker:active {
                                 background-color: transparent;
                                 background-image: none;
                                 border: 0;
                                 border-radius: 0;
                                 box-shadow: none;
                                 min-height: 28px;
                                 min-width: 20px;
                                 padding: 0;
                                 transition: none;
                               }

                               .player-chapter-marker-line {
                                 background-color: #ffffff;
                                 min-height: 10px;
                                 min-width: 1px;
                               }


                               button.player-sponsorblock-skip-button {
                                 border-radius: 999px;
                                 font-weight: 700;
                                 min-height: 32px;
                                 padding: 4px 12px;
                               }

                               button.player-sponsorblock-skip-button.player-sponsorblock-skip-button-sponsor {
                                 background-color: #00d400;
                                 color: #1a1a1a;
                               }

                               button.player-sponsorblock-skip-button.player-sponsorblock-skip-button-selfpromo {
                                 background-color: #ffff00;
                                 color: #1a1a1a;
                               }

                               button.player-sponsorblock-skip-button.player-sponsorblock-skip-button-interaction {
                                 background-color: #cc00ff;
                                 color: #ffffff;
                               }

                               button.player-sponsorblock-skip-button.player-sponsorblock-skip-button-intro {
                                 background-color: #00ffff;
                                 color: #1a1a1a;
                               }

                               button.player-sponsorblock-skip-button.player-sponsorblock-skip-button-outro {
                                 background-color: #0202ed;
                                 color: #ffffff;
                               }

                               button.player-sponsorblock-skip-button.player-sponsorblock-skip-button-preview {
                                 background-color: #008fd6;
                                 color: #ffffff;
                               }

                               button.player-sponsorblock-skip-button.player-sponsorblock-skip-button-hook {
                                 background-color: #395699;
                                 color: #ffffff;
                               }

                               button.player-sponsorblock-skip-button.player-sponsorblock-skip-button-filler {
                                 background-color: #7300FF;
                                 color: #ffffff;
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