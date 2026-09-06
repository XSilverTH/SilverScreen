# Arch Linux dependencies

Supported platform: **x86-64 Arch Linux only**. ARM is unsupported; other
distributions are untested.

## Runtime packages (GirCore 0.8.1)

| Arch package   | Provides                                    |
|----------------|---------------------------------------------|
| `gtk4`         | GTK 4 runtime (GirCore)                     |
| `libadwaita`   | libadwaita (GirCore.Adw-1)                  |
| `webkitgtk-6.0`| WebKitGTK 6.0 (GirCore.WebKit-6.0)          |
| `libsoup3`     | libsoup 3 (GirCore.Soup-3.0)                |
| `libsecret`    | Secret portal support                       |
| `mpv`          | Playback backend (floor `0.41.0`)           |
| `yt-dlp`       | Stream extraction (floor `2026.08.19`)      |

```sh
pacman -S gtk4 libadwaita webkitgtk-6.0 libsoup3 libsecret mpv yt-dlp
```

Floors mirror what Arch x86-64 ships at release time. The app logs the
installed yt-dlp/mpv versions at startup and warns when missing or older;
bump the floor (and the calendar User-Agent override used for yt-dlp
requests) with each release.

## Log-level override

`SILVERSCREEN_LOG_LEVEL` sets the Serilog minimum level. Accepted values
(case-insensitive): `Verbose`/`Debug`, `Information` (default),
`Warning`/`Warn`, `Error`, `Fatal`. Unset or unrecognized falls back to
`Information`.

```sh
SILVERSCREEN_LOG_LEVEL=Debug ./SilverScreen
```

## YoutubeAPI LiveTests environment variables

The vendored `lib/YoutubeAPI` LiveTests are opt-in (see
`YoutubeAPI.LiveTests/LiveTestEnvironment.cs`):

| Variable                        | Effect                                              |
|---------------------------------|-----------------------------------------------------|
| `YOUTUBE_RUN_LIVE_TESTS` (or `YOUTUBE_LIVE_TESTS`, `YOUTUBE_RUN_PUBLIC_TESTS`, `YOUTUBE_PUBLIC_LIVE_TESTS`) | `1`/`true` enables public live tests (also enabled when `YOUTUBE_COOKIES_FILE` points at an existing file) |
| `YOUTUBE_COOKIES_FILE`          | Path to a Netscape-format cookies file; enables authenticated live tests |
| `YOUTUBE_RUN_MUTATION_TESTS`    | `1`/`true` plus a valid cookies file enables mutation live tests |
| `YOUTUBE_MUTATION_CHANNEL_ID`   | Channel used by mutation tests (defaults to the known test channel) |
