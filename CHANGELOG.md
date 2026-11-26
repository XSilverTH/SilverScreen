# Changelog

All notable changes to SilverScreen are documented in this file.

## [0.3.0] - 2026-11-26

### Performance & Startup
- **Faster cold launches:** Persisted InnerTube bootstrap metadata and session-keyed account profile metadata to disk so the app displays the user profile and begins loading feeds immediately without waiting on homepage HTML scrapes or profile network requests.
- **Deferred initialization:** Eliminated cold-start blocking by deferring heavy views and services and optimizing CSS loading.
- **UI & networking efficiency:** Reduced UI-thread churn, timer overhead, and list virtualization costs across browsing, player, and queue views; wired `Video.PlaybackTracking` with watch HTML fallback; optimized mpv IPC, caching, and NativeAOT paths; and updated `YoutubeAPI` with session caching, HTTP/2 connection pooling, and parser optimizations.

### Player
- **Stream delay countdown:** Added a native Cairo/Pango countdown progress bar under the loading spinner when `ytdl_hook` encounters YouTube stream delays, featuring luminance-aware text contrast and smooth crossfades.
- **Authenticated playback & queue sync:** Restored playback session initialization, authentication cookie leases, and cookie forwarding (`--ytdl-raw-options` and libmpv `cookies`/`cookies-file`) for both embedded and external MPV playback, along with live queue mutation synchronization.
- **Fixes:** Resolved subtitle selection stubs, info panel toggle behavior, seek event handling, CSS styling fidelity, and UTC midnight parsing for `upload_date` across non-UTC locales.
- **Architecture:** Decomposed `EmbeddedPlayerView` into 13 modular child Blueprint views and organized player components by feature.

### Browsing & Shell
- **Modular UI architecture:** Decomposed `ChannelView`, `SubscriptionsView`, `VideoCardView`, `AccountPopoverView`, and `MainWindow` into smaller child widgets using `ViewBase`/`BindingScope` and `INavigationService`/`INotificationService`.
- **Stability:** Fixed avatar pixbuf use-after-free and cancellation races in `SubscriptionsChannelPillView` and `ChannelProfileCardView`.

### Privacy & Logging
- **Log anonymization:** Stripped video IDs, titles, watch/media URLs, search queries, channel titles, and `yt-dlp` target URLs from application logs.
- **XDG compliance:** Respected `XDG_STATE_HOME` for log file placement.

### Platform, Dependencies & CI
- **Runtime & tooling:** Upgraded to .NET 11 and C# 15, updated project dependencies, and expanded unit test coverage across navigation, preferences, player engines, formatting, telemetry, and persistent caching.
- **Release CI:** Updated the Arch Linux release workflow to install the .NET 11 SDK via `dotnet-install.sh` until packaged in official repositories.

## [0.2.0] - 2026-11-20

- **Player:** Enabled zero-copy hardware decoding via native Wayland/X11 display handles for libmpv, fixed GLArea fractional scaling calculations using native framebuffer dimensions, and added direct scanout support via `GtkGraphicsOffload` with overlay widget occlusion handling.
- **App:** Reduced memory usage across feeds, thumbnails, and playback, and added GC native memory pressure tracking with widget recycling.
- **Packaging:** Updated AppStream metainfo screenshots.

## [0.1.0] - 2026-09-06

- Initial release.
