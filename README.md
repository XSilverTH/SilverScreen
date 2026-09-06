# SilverScreen
SilverScreen is a GTK 4 and Libadwaita desktop app for finding YouTube videos and playing them in its embedded libmpv player or an external MPV install.

Search YouTube or paste a video link, then play it with your local MPV install. With a YouTube session captured through the isolated in-app Google sign-in or added manually, SilverScreen can also load your Home recommendations.

## What you need

- The .NET 10 SDK.
- GTK 4, Libadwaita, WebKitGTK 6 (`libwebkitgtk-6.0`), and libsoup 3 native libraries compatible with the GirCore bindings used by the app.
- The sibling [`YoutubeAPI`](lib/YoutubeAPI/) project (vendored git submodule) for typed YouTube metadata, search, feeds, comments, ratings, and account operations.
- [`yt-dlp`](https://github.com/yt-dlp/yt-dlp) on `PATH` for stream extraction. Releases pin the yt-dlp version currently shipped by Arch Linux; the app logs the detected yt-dlp and mpv versions at startup and warns if they are missing or older.
- The `libsecret` shared library and an unlocked Freedesktop Secret Service provider, such as GNOME Keyring or KWallet configured with Secret Service support. `secret-tool` is optional for manual diagnostics and is not an application dependency.
- [`mpv`](https://mpv.io/) on `PATH` for external playback (or a working system libmpv for the embedded player).

Supported platform: x86-64 Arch Linux. ARM is unsupported; other distributions are untested. See `PACKAGING.md` and `THIRD-PARTY.md`, shipped alongside this release, for pinned dependency versions, build/packaging notes, and third-party attribution.

The app uses YoutubeAPI for YouTube operations and logs actionable warnings at startup when the configured media extractor, MPV/libmpv, or Secret Service dependencies are unreachable. Install the missing executable or keyring provider, unlock the keyring, and use **Preferences** to correct the media extractor or MPV path before retrying the related action.

## Run it

From the repository root:

```sh
dotnet run --project src/SilverScreen.App/SilverScreen.App.csproj
```

## Basic usage

1. Select the search button in the header.
2. Enter a normal YouTube search or paste a supported YouTube video URL.
3. Select a result to play it in MPV, or open its menu to add it to the queue.

Text searches use YoutubeAPI and show non-Shorts video results. Pasting a regular YouTube video URL skips the search and opens that video directly.

The queue is a small in-memory list and is never persisted. **Add to queue** appends a video to the end of the queue (middle-clicking a video card does the same); the queue popover lets you remove items, clear the list, or **Play all**, and playback auto-advances to the next queued video when auto-advance is enabled in **Preferences**.

## Home recommendations

Home is opt-in because it needs a YouTube session.

1. Open the account button in the header.
2. Choose **Sign in with Google** and complete sign-in in the isolated in-app window.
3. SilverScreen captures the resulting YouTube cookies, closes the sign-in window, and validates the session automatically.
4. Refresh Home.

If embedded Google sign-in is unavailable, choose **Add manual session** instead and paste a browser-exported Netscape-format `cookies.txt` file. SilverScreen stores either session in the logged-in user's desktop Secret Service keyring and restores it on the next app run. The embedded window uses a fresh ephemeral WebKit session for every attempt; its browser storage is discarded after closing, and refreshing never clears the previous saved session unless a new capture succeeds. Cookie values are not shown after saving and no plaintext persistent app configuration is created. Clearing the session removes the keyring entry. When the media extractor or MPV needs the cookies, the app creates a short-lived 0600 cookie file in a 0700 directory and removes it when practical.

## Important details

- Home requires a YouTube session.
- Search results and Home recommendations exclude YouTube Shorts.
- Supported pasted URLs are ordinary YouTube video links. Shorts, channel pages, playlists, and other unsupported YouTube URLs are rejected or reported as not implemented.
- Subscriptions and History require an active YouTube session.
- Only search and queue contents are not persisted; the YouTube session persists in the Secret Service keyring.

## Project layout

| Path | What it contains |
| --- | --- |
| `src/SilverScreen.App/ApplicationServices.cs` | Explicit application composition and disposal owner for the shared queue, session, playback, search, thumbnail, and Home services. |
| `src/SilverScreen.App/Views/Shell` | The thin application-window shell: header chrome, navigation stack, status presentation, global menu, and popover placement. |
| `src/SilverScreen.App/Views/Home`, `Views/Search` | Independently compiled Blueprint page roots and their page-owned rendering/cancellation. |
| `src/SilverScreen.App/Views/Components`, `Views/Popovers` | Reusable video cards plus independent queue and account popover roots. |
| `src/SilverScreen.App/ViewModels` | GTK-free shell, Home, search, queue, and account presentation state/adapters. |
| `src/SilverScreen.App/Features` | Search, playback, queue, session, feed, and thumbnail behavior. |
| `src/SilverScreen.Infrastructure/YouTube` | YoutubeAPI client/session integration and the isolated media-extraction adapter used by MPV. |
| `tests/SilverScreen.Tests` | Unit tests for feature and presentation-state behavior. |


## Feature wishlist
Things I hope to implement (these are big features I'm leaving for when the project is in less of an unstable state):

Commenting on videos (reading comments in the player sidebar already works; posting does not)
Offline playback (downloading)
Managing offline (or online) playback (organize videos and make playlists and stuff)

I hope to make this a better and complete replacement for the YouTube website. not just an alternative


## Development

Clone with submodules (the vendored `lib/YoutubeAPI` and `lib/Hi3Helper.SharpDiscordRPC` checkouts are required to build):

```sh
git clone --recurse-submodules <repo-url>
# inside an existing checkout:
git submodule update --init --recursive
```

Requires the .NET 10 SDK or newer.

Build the solution:

```sh
dotnet build SilverScreen.slnx
```

Run the app tests:

```sh
dotnet test tests/SilverScreen.Tests/SilverScreen.Tests.csproj
```

`lib/YoutubeAPI` is a separate solution (`lib/YoutubeAPI/YoutubeAPI.slnx`) with its own test suite; run its tests from that directory rather than from this solution.

This README covers the application and its usual local setup. The source is the current reference for implementation details and default timeouts.
