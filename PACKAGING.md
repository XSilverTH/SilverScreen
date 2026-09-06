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

`silverscreen-<version>-linux-x64.tar.gz` contains a standard Unix hierarchy
and an installation script:

- `install.sh` — installation script supporting `--user` (`~/.local`), custom prefixes (`PREFIX=...`), or uninstall
- `bin/SilverScreen` — the published binary
- `share/applications/io.github.silverscreen.SilverScreen.desktop`
- `share/metainfo/io.github.silverscreen.SilverScreen.metainfo.xml`
- `share/icons/hicolor/scalable/apps/io.github.silverscreen.SilverScreen.svg` — application icon
- `share/licenses/silverscreen/LICENSE`
- `share/doc/silverscreen/THIRD-PARTY.md`
- `LICENSE`, `THIRD-PARTY.md`
## AUR notes

`packaging/PKGBUILD` is a `silverscreen-git` package tracking `main` via a
`git+` source and a `git describe`-based `pkgver()` (`arch=(x86_64)`,
`provides`/`conflicts` `silverscreen`). It needs a maintainer test in a
clean chroot before any AUR upload.

A stable `silverscreen` release package from the tag tarball staged by
`.github/workflows/tag.yml` is next once the current tag is published; a
commented sketch is kept at the bottom of `packaging/PKGBUILD`. It likewise
needs a maintainer chroot test, including filling in `sha256sums`, before
any AUR upload.
