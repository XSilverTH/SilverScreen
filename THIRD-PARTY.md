# Third-party attributions

SilverScreen 0.1.0 bundles or depends on the following third-party projects.
Only attribution is given here; see each upstream for its full license text.

## Bundled (vendored git submodules)

| Component | License | Upstream |
|-----------|---------|----------|
| `lib/YoutubeAPI` — typed YouTube metadata, search, feeds, comments, ratings, and account operations | LGPL-3.0 (`lib/YoutubeAPI/LICENSE`) | https://github.com/XSilverTH/YoutubeAPI |
| `lib/Hi3Helper.SharpDiscordRPC` — Discord Rich Presence client | MIT (`lib/Hi3Helper.SharpDiscordRPC/LICENSE`) | https://github.com/CollapseLauncher/Hi3Helper.SharpDiscordRPC (fork of https://github.com/Lachee/discord-rpc-csharp) |

## .NET packages

| Component | Version | License | Upstream |
|-----------|---------|---------|----------|
| GirCore (`GirCore.Adw-1`, `GirCore.Soup-3.0`, `GirCore.WebKit-6.0`) — GTK/Adwaita/WebKit bindings | 0.8.1 | MIT | https://github.com/gircore/gircore |
| Serilog (+ `Serilog.Sinks.Console`, `Serilog.Sinks.File`) — logging | 4.4.0 / 6.1.1 / 7.0.0 | Apache-2.0 | https://github.com/serilog/serilog |
| `Microsoft.Extensions.DependencyInjection` (+ Abstractions) | 10.0.11 | MIT | https://github.com/dotnet/runtime |
| `Tmds.DBus.Protocol` (+ Generator) — D-Bus / MPRIS wiring | 0.95.0 | See upstream | https://github.com/tmds/Tmds.DBus |
| `XSTH.Blueprint.Helpers` — Blueprint UI helpers | 3.0.0 | See package page | https://www.nuget.org/packages/XSTH.Blueprint.Helpers |

## External runtime tools

| Component | License | Upstream |
|-----------|---------|----------|
| yt-dlp — media stream extraction for MPV (must be on `PATH`) | Unlicense | https://github.com/yt-dlp/yt-dlp |
| mpv — video playback, built-in libmpv or external binary (must be on `PATH`) | GPL-2.0-or-later | https://mpv.io/ |

## System libraries (Arch packages)

GTK 4, Libadwaita, WebKitGTK 6, libsoup3, and libsecret are LGPL-2.1-or-later
system libraries loaded at runtime; see https://www.gtk.org/,
https://gnome.pages.gitlab.gnome.org/libadwaita/,
https://webkitgtk.org/, https://libsoup.org/, and
https://wiki.gnome.org/Projects/Libsecret.
