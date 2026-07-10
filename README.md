# SilverScreen

SilverScreen is a Linux-native YouTube client built with C#, GIR.Core, GTK4, libadwaita, and Blueprint views.

## What works

- Search YouTube with `yt-dlp`; paste a supported YouTube video URL to play it directly.
- Launch playable videos in external `mpv`; a manual Netscape `cookies.txt` session is passed through a user-only temporary file when present.
- Load recommendations with `yt-dlp`'s `:ytrec` extractor after adding a manual session. The app falls back to public recommendations only when an authenticated request returns no videos.
- Cache thumbnails under `$XDG_CACHE_HOME/SilverScreen/thumbnails` (or `~/.cache/SilverScreen/thumbnails`) with a bounded on-disk cache.
- Add, reorder, remove, and clear an in-memory queue. Use a card's overflow menu to copy its playable video link.

Sessions and queues are intentionally process-local: cookie content is not persisted and is removed from temporary subprocess files when their process exits.

## Not implemented

Subscriptions, history, playlists, channel browsing, browser-based sign-in, embedded playback, SponsorBlock, and persistent application state.

## Prerequisites

- .NET SDK 10.
- GTK 4 and libadwaita runtime libraries.
- `yt-dlp` for search and Home recommendations.
- `mpv` for playback.

On Arch-based distributions:

```sh
sudo pacman -S dotnet-sdk gtk4 libadwaita yt-dlp mpv
```

## Build

```sh
dotnet restore SilverScreen.sln
dotnet build SilverScreen.sln --no-restore
```

## Run

```sh
dotnet run --project src/SilverScreen.App/SilverScreen.App.csproj --no-build
```

## Use

1. Use the search button to search by text or paste a YouTube video URL. Click a card to play it, middle-click to queue it, or use its overflow menu for more actions.
2. Open the account menu to paste Netscape `cookies.txt` content. The app stores it only in memory for the current process.
3. After adding a session, use Home's refresh button to request recommendations. Use **Validate Home session** in the account menu to check the current session.
4. Install `yt-dlp` and `mpv` before using search/Home and playback respectively; missing executables produce an in-app error rather than a shell invocation.

## Test

```sh
dotnet test SilverScreen.sln --no-restore
```
