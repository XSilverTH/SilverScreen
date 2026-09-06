# Packaging SilverScreen

Supported platform: **Arch Linux, x86_64 only.** ARM is unsupported and other
distributions are untested. There is no Flatpak packaging; nothing here blocks
someone from contributing it.

## Reproducible publish

From the repository root, with the .NET 10 SDK installed:

```sh
DOTNET_CLI_TELEMETRY_OPTOUT=1 \
dotnet publish src/SilverScreen.App/SilverScreen.App.csproj \
  -c Release -r linux-x64 --self-contained
```

`PublishAot` is set in the application project, so the output is an
ahead-of-time compiled, self-contained `SilverScreen` binary. Tagging `vX.Y.Z`
in git triggers `.github/workflows/tag.yml`, which runs the same publish,
stages the tarball below, and uploads it to the GitHub Release.

## Required Arch packages (GirCore 0.8.1 native deps)

| Arch package       | Provides                        |
|--------------------|---------------------------------|
| `gtk4`             | GTK 4 runtime                   |
| `libadwaita`       | Libadwaita widgets              |
| `webkitgtk-6.0`    | WebKitGTK 6 (in-app sign-in)    |
| `libsoup3`         | HTTP stack for WebKitGTK        |
| `libsecret`        | Credential storage              |
| `mpv`              | Playback (built-in libmpv or external binary) |
| `yt-dlp`           | Stream extraction for MPV       |
| `hicolor-icon-theme` | Icon theme layout             |
| `dotnet-sdk>=10`   | Build only                      |

## Tarball layout

`silverscreen-<version>-linux-x64.tar.gz` contains a single top-level
directory with:

- `SilverScreen` — the published binary
- `io.github.silverscreen.SilverScreen.desktop`
- `io.github.silverscreen.SilverScreen.metainfo.xml`
- `silverscreen.svg` — application icon
- `LICENSE`, `THIRD-PARTY.md`

## AUR notes

`packaging/PKGBUILD` is a sketch for a `silverscreen` release package
(`pkgver=0.1.0`, `arch=(x86_64)`). It is marked as needing maintainer testing
in a clean chroot before any AUR upload, including filling in `sha256sums`.
A `-git` variant would track `main` via a `git+` source and a
`git describe`-based `pkgver()`; see the comment at the bottom of the file.
