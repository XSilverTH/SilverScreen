# SilverScreen
HERE BE DRAGONS: this is extremely early in development and is missing most of it's planned features. if you want, you can use this daily as its not critical infrastructure, but I wouldn't.

SilverScreen is a GTK 4 and Libadwaita desktop app for finding YouTube videos and opening them in MPV (hopefully an embeded libmpv player in the future).

Search YouTube or paste a video link, then play it with your local MPV install. If you add a temporary YouTube cookie session, SilverScreen can also load your Home recommendations.

## What you need

- The .NET 10 SDK.
- GTK 4 and Libadwaita native libraries compatible with the GirCore bindings used by the app.
- [`yt-dlp`](https://github.com/yt-dlp/yt-dlp) on `PATH` for search and Home recommendations.
- [`mpv`](https://mpv.io/) on `PATH` for playback.

The app launches `yt-dlp` and `mpv` by those names. If either command is missing, the related action cannot run.

## Run it

From the repository root:

```sh
dotnet restore
dotnet run --project src/SilverScreen.App/SilverScreen.App.csproj
```

## Basic usage

1. Select the search button in the header.
2. Enter a normal YouTube search or paste a supported YouTube video URL.
3. Select a result to play it in MPV, or open its menu to add it to the queue.

Text searches use `yt-dlp` and show up to 20 non-Shorts video results. Pasting a regular YouTube video URL skips the search and opens that video in MPV.

The queue is a small in-memory list. **Add next** places a video at the front; you can remove items or clear the list from the floating queue button. It does nothing for now. in the future I hope to turn it into something used in place of opening multiple tabs to watch back to back.

## Home recommendations

Home is opt-in because it needs a YouTube session.

1. Open the account button in the header.
2. Choose **Add manual session**.
3. Paste the contents of a browser-exported Netscape-format `cookies.txt` file and save it.
4. Choose **Validate Home session**, then refresh Home.

SilverScreen keeps this session only in memory for the current process. Cookie values are not shown after saving and are not written to a permanent app config. When `yt-dlp` or MPV needs them, the app creates a temporary cookie file with user-only permissions.

## Important details

- Home requires a manual session. It fetches one recommendation page; loading additional Home pages is not implemented.
- Search results and Home recommendations exclude YouTube Shorts.
- Supported pasted URLs are ordinary YouTube video links. Shorts, channel pages, playlists, and other unsupported YouTube URLs are rejected or reported as not implemented.
- Subscriptions and History are currently placeholder views. Preferences and About are also placeholders.
- Search, queue contents, and the manual session are not persisted between app runs.

## Project layout

| Path | What it contains |
| --- | --- |
| `src/SilverScreen.App/Views` | The GTK/Libadwaita window and interaction code. |
| `src/SilverScreen.App/Features` | Search, playback, queue, session, feed, and thumbnail behavior. |
| `src/SilverScreen.App/Infrastructure/YouTube` | The `yt-dlp`-backed Home feed client. |
| `tests/SilverScreen.Tests` | Unit tests for the app behavior. |


## Feature wishlist
Things I hope to implement (these are big features I'm leaving for when the project is in less of an unstable state):

Viewing video comments  
Embedded player (probably with libmpv)  
Offline playback (downloading)  
Managing offline (or online) playback (organize videos and make playlists and stuff)  
sCommenting on videos

I hope to make this a better and complete replacement for the YouTube website. not just an alternative


## Development

Build the solution:

```sh
dotnet build SilverScreen.sln
```

Run the tests:

```sh
dotnet test tests/SilverScreen.Tests/SilverScreen.Tests.csproj
```

This README covers the application and its usual local setup. The source is the current reference for implementation details and default timeouts.
