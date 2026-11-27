# Changelog

All notable changes to SilverScreen are documented in this file.

## [0.4.0] - 2026-11-27

### Offline Library & Downloads

- **Built-in downloader & Library tab:** Added a **Library** tab powered by a `yt-dlp` worker pool with configurable concurrency, live progress/speed/ETA tracking, pause and resume across restarts, retry, and cancel cleanup.
- **Presets & download options:** Added a download options dialog (quality, codec, container, audio-only, embedded/sidecar subtitles, metadata and thumbnail embedding, and SponsorBlock modes), named presets, and a **Downloads** page in Preferences. Downloads can be started from video card menus, the player header, or Watch Queue (**Download all**) into either the managed Library (`~/Videos/SilverScreen` by default) or any custom folder.
- **Local-first playback & offline indicators:** Automatically plays downloaded Library copies in both the embedded player and external `mpv` (indicating online-only qualities in the selector and notifying on quality mismatches), stores SponsorBlock segments locally for offline skipping, continues loading online comments and ratings when connected, and displays an offline badge on downloaded videos in feeds.

### Comments

- **Threaded replies & card redesign:** Added hierarchical comment trees with thread lines and on-demand reply loading, alongside redesigned comment cards with header like controls and a unified footer reply bar.
- **Commenting & engagement:** Added a comment composer to the sidebar and support for posting, replying, editing, deleting, and liking comments.
- **Translation & readability:** Added on-demand comment translation with automatic language detection, a configurable YouTube language (`hl`) preference, and expand/collapse toggles for long comments (fixing a text clipping crash and empty short comments).

### Channels & Player

- **Channel subscriptions:** Added subscribe and unsubscribe support on channel pages, backed by updated channel ID extraction in `YoutubeAPI`.
- **Player & seeking fixes:**
  - Enabled `gtk-primary-button-warps-slider` so clicking the timeline seeks directly on KDE Plasma.
  - Scoped hit-testing on the description cue so it no longer blocks clicks on the progress bar and playhead.
  - Hid center transport controls while loading so they no longer overlap the spinner.
  - Focused the embedded player when re-selecting an already-playing video.

### Visual Polish & Platform

- **Bundled symbolic icons:** Bundled app symbolic icons—including a monochrome `subscriptions-symbolic` icon to prevent theme fallbacks to colored emojis—and minified SVG assets.
- **Runtime async:** Enabled .NET 11 `runtime-async` across `SilverScreen.App`, `SilverScreen.Core`, and `SilverScreen.Infrastructure`.

## [0.3.0] - 2026-11-26

### Performance & Startup

- **Faster cold launches:** Persisted InnerTube bootstrap metadata and session-keyed account profile metadata to disk so
  the app displays the user profile and begins loading feeds immediately without waiting on homepage HTML scrapes or
  profile network requests.
- **Deferred initialization:** Eliminated cold-start blocking by deferring heavy views and services and optimizing CSS
  loading.
- **UI & networking efficiency:** Reduced UI-thread churn, timer overhead, and list virtualization costs across
  browsing, player, and queue views; wired `Video.PlaybackTracking` with watch HTML fallback; optimized mpv IPC,
  caching, and NativeAOT paths; and updated `YoutubeAPI` with session caching, HTTP/2 connection pooling, and parser
  optimizations.

### Player

- **Stream delay countdown:** Added a native Cairo/Pango countdown progress bar under the loading spinner when
  `ytdl_hook` encounters YouTube stream delays, featuring luminance-aware text contrast and smooth crossfades.
- **Authenticated playback & queue sync:** Restored playback session initialization, authentication cookie leases, and
  cookie forwarding (`--ytdl-raw-options` and libmpv `cookies`/`cookies-file`) for both embedded and external MPV
  playback, along with live queue mutation synchronization.
- **Fixes:** Resolved subtitle selection stubs, info panel toggle behavior, seek event handling, CSS styling fidelity,
  and UTC midnight parsing for `upload_date` across non-UTC locales.
- **Architecture:** Decomposed `EmbeddedPlayerView` into 13 modular child Blueprint views and organized player
  components by feature.

### Browsing & Shell

- **Modular UI architecture:** Decomposed `ChannelView`, `SubscriptionsView`, `VideoCardView`, `AccountPopoverView`, and
  `MainWindow` into smaller child widgets using `ViewBase`/`BindingScope` and `INavigationService`/
  `INotificationService`.
- **Stability:** Fixed avatar pixbuf use-after-free and cancellation races in `SubscriptionsChannelPillView` and
  `ChannelProfileCardView`.

### Privacy & Logging

- **Log anonymization:** Stripped video IDs, titles, watch/media URLs, search queries, channel titles, and `yt-dlp`
  target URLs from application logs.
- **XDG compliance:** Respected `XDG_STATE_HOME` for log file placement.

### Platform, Dependencies & CI

- **Runtime & tooling:** Upgraded to .NET 11 and C# 15, updated project dependencies, and expanded unit test coverage
  across navigation, preferences, player engines, formatting, telemetry, and persistent caching.
- **Release CI:** Updated the Arch Linux release workflow to install the .NET 11 SDK via `dotnet-install.sh` until
  packaged in official repositories.

## [0.2.0] - 2026-11-20

- **Player:** Enabled zero-copy hardware decoding via native Wayland/X11 display handles for libmpv, fixed GLArea
  fractional scaling calculations using native framebuffer dimensions, and added direct scanout support via
  `GtkGraphicsOffload` with overlay widget occlusion handling.
- **App:** Reduced memory usage across feeds, thumbnails, and playback, and added GC native memory pressure tracking
  with widget recycling.
- **Packaging:** Updated AppStream metainfo screenshots.

## [0.1.0] - 2026-09-06

- Initial release.
