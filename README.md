# SilverScreen

A GTK 4 / Libadwaita YouTube client for Linux. It uses `yt-dlp` to grab streams and plays them through a built-in player (or an external `mpv` window if you prefer). 

This is an early alpha. Expect bugs. I build and test this on CachyOS, so other distros might be hit or miss.

It supports personalized feeds but you can also use it without an account. If you sign in, you get access to your Home feed, Subscriptions, and Watch History. if you don't you still can search videos, browse channels, or just paste links directly.  it'll store your session in your system keyring.

It also has a basic queue, SponsorBlock support, comments, dislikes/likes (via RYD), and Discord Rich Presence.

## Screenshots

<table>
  <thead>
    <tr>
      <th width="33.3%" align="center">Subscriptions</th>
      <th width="33.3%" align="center">Player</th>
      <th width="33.3%" align="center">Comments</th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td><a href="screenshots/Subscriptions.webp"><img src="screenshots/Subscriptions.webp" alt="Subscriptions" /></a></td>
      <td><a href="screenshots/Player.webp"><img src="screenshots/Player.webp" alt="Player" /></a></td>
      <td><a href="screenshots/Comments.webp"><img src="screenshots/Comments.webp" alt="Comments" /></a></td>
    </tr>
  </tbody>
</table>

## Installation

### Arch Linux (AUR)

Install the precompiled binary package using your AUR helper:

```sh
yay -S silverscreen-bin
# or
paru -S silverscreen-bin
```

Or build the latest git version from source:

```sh
yay -S silverscreen-git
```

### Other Linux Distributions (AppImage)

Grab the latest standalone **AppImage** from the [GitHub Releases](https://github.com/XSilverTH/SilverScreen/releases):

```sh
chmod +x SilverScreen-x86_64.AppImage
./SilverScreen-x86_64.AppImage
```
## Building manually

you'll need .NET 10 SDK, blueprint-compiler, clang, glib2, gtk4, libadwaita, webkitgtk-6.0, libsoup3, mpv, and yt-dlp.

```sh
git clone --recurse-submodules https://github.com/XSilverTH/SilverScreen.git
cd SilverScreen

dotnet publish src/SilverScreen.App/SilverScreen.App.csproj /p:DebugType=None /p:DebugSymbols=false -o publish
```

## Todo

* Posting comments (reading them in the sidebar already works)

* Downloading videos for offline playback

* Youtube playlist support

## License

[GPLv3](LICENSE)
