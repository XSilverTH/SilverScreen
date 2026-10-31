#!/bin/sh
set -eu

REPO_URLS_DEFAULT="https://github.com/XSilverTH/SilverScreen https://fjo.xsth.space/XSilverTH/SilverScreen"

show_help() {
  cat << 'EOF'
Usage: install.sh [OPTIONS]

Options:
  --user              Install to user local directory (~/.local)
  --prefix <PATH>     Install prefix (default: /usr/local, or ~/.local with --user)
  -p <PATH>           Short alias for --prefix
  --destdir <PATH>    Staging root directory for packaging
  -d <PATH>           Short alias for --destdir
  --uninstall         Uninstall SilverScreen from prefix
  --repo-url <URL>    Git repository URL used when cloning in standalone mode
  --keep-build-dir    Keep the cloned build directory after installation
  --skip-deps         Skip distro detection and prerequisite installation
  --no-pkgbuild       Skip PKGBUILD packaging on Arch-based distros (use direct install)
  -h, --help          Show this help message

Environment variables:
  PREFIX              Target prefix (default: /usr/local)
  DESTDIR             Staging root directory for packaging
  REPO_URL            Git repository URL for standalone clones (same as --repo-url)
EOF
  exit 0
}

USER_MODE=0
UNINSTALL=0
SKIP_DEPS=0
KEEP_BUILD_DIR=0
USE_PKGBUILD=1
PREFIX_SET=0
if [ -n "${PREFIX:-}" ] && [ "$PREFIX" != "/usr/local" ]; then
  PREFIX_SET=1
fi
REPO_URL="${REPO_URL:-}"
PREFIX="${PREFIX:-/usr/local}"
DESTDIR="${DESTDIR:-}"
while [ $# -gt 0 ]; do
  case "$1" in
    --user)
      USER_MODE=1
      shift
      ;;
    --prefix)
      [ $# -gt 1 ] || { echo "Error: --prefix requires an argument" >&2; exit 1; }
      PREFIX="$2"
      PREFIX_SET=1
      shift 2
      ;;
    -p)
      [ $# -gt 1 ] || { echo "Error: -p requires an argument" >&2; exit 1; }
      PREFIX="$2"
      PREFIX_SET=1
      shift 2
      ;;
    --prefix=*)
      PREFIX="${1#*=}"
      PREFIX_SET=1
      shift
      ;;
    --destdir)
      [ $# -gt 1 ] || { echo "Error: --destdir requires an argument" >&2; exit 1; }
      DESTDIR="$2"
      shift 2
      ;;
    -d)
      [ $# -gt 1 ] || { echo "Error: -d requires an argument" >&2; exit 1; }
      DESTDIR="$2"
      shift 2
      ;;
    --destdir=*)
      DESTDIR="${1#*=}"
      shift
      ;;
    --uninstall)
      UNINSTALL=1
      shift
      ;;
    --repo-url)
      [ $# -gt 1 ] || { echo "Error: --repo-url requires an argument" >&2; exit 1; }
      REPO_URL="$2"
      shift 2
      ;;
    --repo-url=*)
      REPO_URL="${1#*=}"
      shift
      ;;
    --keep-build-dir)
      KEEP_BUILD_DIR=1
      shift
      ;;
    --skip-deps)
      SKIP_DEPS=1
      shift
      ;;
    --no-pkgbuild)
      USE_PKGBUILD=0
      shift
      ;;
    -h|--help)
      show_help
      ;;
    *)
      echo "Unknown option: $1" >&2
      echo "Run 'install.sh --help' for usage." >&2
      exit 1
      ;;
  esac
done

if [ "$USER_MODE" -eq 1 ]; then
  PREFIX="${HOME}/.local"
fi

# Locate source directory and files
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

REPO_ROOT=""
if [ -f "${SCRIPT_DIR}/SilverScreen.slnx" ]; then
  REPO_ROOT="${SCRIPT_DIR}"
elif [ -f "${SCRIPT_DIR}/../SilverScreen.slnx" ]; then
  REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
fi

PKG_BUILD_DIR=""
TEMP_DIR=""
cleanup() {
  if [ -n "${PKG_BUILD_DIR}" ]; then
    if [ "${KEEP_BUILD_DIR}" -eq 1 ]; then
      echo "Keeping PKGBUILD build directory at: ${PKG_BUILD_DIR}"
    else
      rm -rf "${PKG_BUILD_DIR}"
    fi
  fi
  if [ -n "${TEMP_DIR}" ]; then
    if [ "${KEEP_BUILD_DIR}" -eq 1 ]; then
      echo "Keeping cloned build directory at: ${TEMP_DIR}"
    else
      rm -rf "${TEMP_DIR}"
    fi
  fi
}
trap cleanup EXIT

DISTRO_ID=""
DISTRO_LIKE=""
DISTRO_NAME=""

detect_distro() {
  if [ -r /etc/os-release ]; then
    # shellcheck disable=SC1091
    . /etc/os-release || true
    DISTRO_ID="${ID:-}"
    DISTRO_LIKE="${ID_LIKE:-}"
    DISTRO_NAME="${PRETTY_NAME:-${ID:-unknown}}"
  else
    DISTRO_NAME="unknown (no /etc/os-release)"
  fi
}

is_arch_distro() {
  case "${DISTRO_ID} ${DISTRO_LIKE}" in
    *arch*|*artix*|*manjaro*|*endeavouros*|*cachyos*|*garuda*)
      return 0
      ;;
    *)
      if command -v pacman >/dev/null 2>&1 && command -v makepkg >/dev/null 2>&1; then
        return 0
      fi
      return 1
      ;;
  esac
}

run_priv() {
  if [ "$(id -u)" -eq 0 ]; then
    "$@"
  elif command -v sudo >/dev/null 2>&1; then
    sudo "$@"
  elif command -v doas >/dev/null 2>&1; then
    doas "$@"
  else
    return 1
  fi
}

TARGET_BIN_DIR="${DESTDIR}${PREFIX}/bin"
TARGET_APP_DIR="${DESTDIR}${PREFIX}/share/applications"
TARGET_META_DIR="${DESTDIR}${PREFIX}/share/metainfo"
TARGET_ICON_DIR="${DESTDIR}${PREFIX}/share/icons/hicolor/scalable/apps"
TARGET_LIC_DIR="${DESTDIR}${PREFIX}/share/licenses/silverscreen"
TARGET_DOC_DIR="${DESTDIR}${PREFIX}/share/doc/silverscreen"

update_caches() {
  if [ -z "${DESTDIR}" ]; then
    if command -v update-desktop-database >/dev/null 2>&1; then
      update-desktop-database -q "${TARGET_APP_DIR}" 2>/dev/null || true
    fi
    if command -v gtk-update-icon-cache >/dev/null 2>&1; then
      gtk-update-icon-cache -q -t -f "${DESTDIR}${PREFIX}/share/icons/hicolor" 2>/dev/null || true
    fi
  fi
}

if [ "$UNINSTALL" -eq 1 ]; then
  detect_distro
  if [ "$USE_PKGBUILD" -eq 1 ] && [ "$USER_MODE" -eq 0 ] && { [ "$PREFIX_SET" -eq 0 ] || [ "$PREFIX" = "/usr" ]; } && is_arch_distro && command -v pacman >/dev/null 2>&1; then
    if pacman -Qq silverscreen-git >/dev/null 2>&1; then
      echo "Uninstalling silverscreen-git package via pacman..."
      run_priv pacman -R --noconfirm silverscreen-git
      exit 0
    elif pacman -Qq silverscreen >/dev/null 2>&1; then
      echo "Uninstalling silverscreen package via pacman..."
      run_priv pacman -R --noconfirm silverscreen
      exit 0
    fi
  fi

  echo "Uninstalling SilverScreen from ${DESTDIR}${PREFIX}..."
  rm -f "${TARGET_BIN_DIR}/SilverScreen"
  rm -f "${TARGET_BIN_DIR}/silverscreen"
  rm -f "${TARGET_APP_DIR}/io.github.silverscreen.SilverScreen.desktop"
  rm -f "${TARGET_META_DIR}/io.github.silverscreen.SilverScreen.metainfo.xml"
  rm -f "${TARGET_ICON_DIR}/io.github.silverscreen.SilverScreen.svg"
  rm -f "${TARGET_LIC_DIR}/LICENSE"
  rm -f "${TARGET_LIC_DIR}/DiscordRPC.LICENSE"
  rm -f "${TARGET_LIC_DIR}/YoutubeAPI.LICENSE"
  rmdir "${TARGET_LIC_DIR}" 2>/dev/null || true
  rm -f "${TARGET_DOC_DIR}/THIRD-PARTY.md"
  rmdir "${TARGET_DOC_DIR}" 2>/dev/null || true
  update_caches
  echo "SilverScreen uninstalled successfully."
  exit 0
fi

detect_distro

# --------------------------------------------------------------------------
# Arch-based distribution packaging via PKGBUILD
# --------------------------------------------------------------------------

if [ "$USE_PKGBUILD" -eq 1 ] && [ "$USER_MODE" -eq 0 ] && [ -z "${DESTDIR}" ] && { [ "$PREFIX_SET" -eq 0 ] || [ "$PREFIX" = "/usr" ]; } && is_arch_distro; then
  if ! command -v makepkg >/dev/null 2>&1; then
    echo "Warning: makepkg not found on this Arch-based system; falling back to direct installation." >&2
  else
    RUN_AS_USER=""
    CAN_RUN_MAKEPKG=1
    if [ "$(id -u)" -eq 0 ]; then
      if [ -n "${SUDO_USER:-}" ] && [ "${SUDO_USER}" != "root" ]; then
        RUN_AS_USER="${SUDO_USER}"
      else
        echo "Warning: makepkg cannot run as root and no regular user was detected via SUDO_USER." >&2
        echo "Falling back to direct installation." >&2
        CAN_RUN_MAKEPKG=0
      fi
    fi

    if [ "$CAN_RUN_MAKEPKG" -eq 1 ]; then
      echo "Detected Arch-based distribution (${DISTRO_NAME}). Using PKGBUILD with makepkg..."

      if ! command -v fakeroot >/dev/null 2>&1; then
        echo "fakeroot not found; installing base-devel group needed by makepkg..."
        if ! run_priv pacman -Sy --needed --noconfirm base-devel; then
          echo "Warning: failed to install base-devel; makepkg may fail." >&2
        fi
      fi

      PKGBUILD_SRC=""
      for p in \
        "${SCRIPT_DIR}/PKGBUILD" \
        "${SCRIPT_DIR}/packaging/PKGBUILD" \
        "${REPO_ROOT}/packaging/PKGBUILD" \
        "${REPO_ROOT}/PKGBUILD"; do
        if [ -n "$p" ] && [ -f "$p" ]; then
          PKGBUILD_SRC="$p"
          break
        fi
      done

      if [ -z "${PKGBUILD_SRC}" ]; then
        TEMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/silverscreen-build.XXXXXX")"
        urls="${REPO_URL}"
        [ -n "${urls}" ] || urls="${REPO_URLS_DEFAULT}"
        for url in ${urls}; do
          echo "Cloning ${url} to retrieve PKGBUILD..."
          if git clone --recurse-submodules "${url}" "${TEMP_DIR}/repo" 2>&1; then
            if [ -f "${TEMP_DIR}/repo/packaging/PKGBUILD" ]; then
              PKGBUILD_SRC="${TEMP_DIR}/repo/packaging/PKGBUILD"
              break
            fi
          fi
        done
      fi

      if [ -z "${PKGBUILD_SRC}" ]; then
        echo "Warning: PKGBUILD could not be found; falling back to direct installation." >&2
      else
        PKG_BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/silverscreen-pkgbuild.XXXXXX")"
        cp "${PKGBUILD_SRC}" "${PKG_BUILD_DIR}/PKGBUILD"

        if [ -n "${REPO_URL}" ]; then
          clean_url="${REPO_URL%.git}"
          sed -i "s|^url=.*|url='${clean_url}'|" "${PKG_BUILD_DIR}/PKGBUILD"
        fi

        if [ -n "${RUN_AS_USER}" ]; then
          chown -R "${RUN_AS_USER}:" "${PKG_BUILD_DIR}"
        fi

        MAKEPKG_FLAGS="-f"
        if [ "${SKIP_DEPS}" -eq 1 ]; then
          MAKEPKG_FLAGS="${MAKEPKG_FLAGS} -d"
        else
          MAKEPKG_FLAGS="${MAKEPKG_FLAGS} -s --noconfirm"
        fi

        echo "Building SilverScreen package using makepkg (this can take a while)..."
        build_ok=1
        if [ -n "${RUN_AS_USER}" ]; then
          if ! (cd "${PKG_BUILD_DIR}" && sudo -H -u "${RUN_AS_USER}" makepkg ${MAKEPKG_FLAGS}); then
            build_ok=0
          fi
        else
          if ! (cd "${PKG_BUILD_DIR}" && makepkg ${MAKEPKG_FLAGS}); then
            build_ok=0
          fi
        fi

        if [ "$build_ok" -eq 0 ]; then
          echo "Error: makepkg failed to build the package." >&2
          exit 1
        fi

        BUILT_PKGS="$(find "${PKG_BUILD_DIR}" -maxdepth 1 -name "*.pkg.tar.zst" ! -name "*-debug-*.pkg.tar.zst")"
        if [ -z "${BUILT_PKGS}" ]; then
          BUILT_PKGS="$(find "${PKG_BUILD_DIR}" -maxdepth 1 -name "*.pkg.tar.zst")"
        fi
        if [ -z "${BUILT_PKGS}" ]; then
          echo "Error: no .pkg.tar.zst package produced by makepkg." >&2
          exit 1
        fi

        echo "Installing SilverScreen package via pacman..."
        # shellcheck disable=SC2086
        if ! run_priv pacman -U --noconfirm ${BUILT_PKGS}; then
          echo "Error: pacman failed to install package." >&2
          exit 1
        fi

        echo "SilverScreen installed successfully via pacman (PKGBUILD)."
        exit 0
      fi
    fi
  fi
fi

manual_prereq_message() {
  cat << 'EOF'
Could not install prerequisites automatically.

Install the following manually, then re-run install.sh:

  Runtime:
    GTK 4, Libadwaita, WebKitGTK 6.0, libsoup 3, libsecret, mpv, yt-dlp,
    hicolor-icon-theme
  Build:
    git, .NET 10 SDK, clang, zlib development headers, glib2 development
    tools (providing glib-compile-resources), blueprint-compiler

The .NET 10 SDK can be installed with the official script if your distro
does not package it:

  curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
EOF
}

confirm_continue() {
  printf 'Continue building anyway (assumes prerequisites are installed)? [y/N] '
  read -r reply || { echo "" >&2; return 1; }
  case "$reply" in
    y|Y|yes|Yes|YES) return 0 ;;
    *) return 1 ;;
  esac
}

ensure_prereqs() {
  detect_distro
  echo "Detected distribution: ${DISTRO_NAME}"

  missing=""
  for cmd in git clang blueprint-compiler glib-compile-resources; do
    command -v "$cmd" >/dev/null 2>&1 || missing="$missing $cmd"
  done
  if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
    missing="$missing dotnet-sdk-10"
  fi

  if [ -z "${missing}" ]; then
    echo "All build prerequisites are already installed."
    return 0
  fi
  echo "Missing build prerequisites:${missing}"

  PKG_MGR=""
  PKGS=""
  case "${DISTRO_ID} ${DISTRO_LIKE}" in
    *arch*|*artix*|*manjaro*|*endeavouros*|*cachyos*|*garuda*)
      PKG_MGR="pacman"
      PKGS="git dotnet-sdk clang glib2 blueprint-compiler gtk4 libadwaita webkitgtk-6.0 libsoup3 libsecret mpv yt-dlp hicolor-icon-theme"
      ;;
    *debian*|*ubuntu*|*mint*|*pop*|*neon*|*zorin*|*raspbian*|*kali*)
      PKG_MGR="apt"
      PKGS="git dotnet-sdk-10.0 clang zlib1g-dev libglib2.0-dev blueprint-compiler libgtk-4-1 libadwaita-1-0 libwebkitgtk-6.0-4 libsoup-3.0-0 libsecret-1-0 mpv yt-dlp"
      ;;
    *fedora*|*rhel*|*centos*|*rocky*|*alma*|*nobara*)
      PKG_MGR="dnf"
      PKGS="git dotnet-sdk-10.0 clang zlib-devel glib2-devel blueprint-compiler gtk4 libadwaita webkitgtk6.0 libsoup3 libsecret mpv yt-dlp"
      ;;
    *suse*)
      PKG_MGR="zypper"
      PKGS="git dotnet-sdk-10.0 clang zlib-devel glib2-devel blueprint-compiler gtk4 libadwaita-1-0 webkitgtk-6.0 libsoup-3-0 libsecret-1-0 mpv yt-dlp"
      ;;
    *alpine*)
      PKG_MGR="apk"
      PKGS="git dotnet-sdk-10 clang zlib-dev glib2-dev blueprint-compiler gtk4.0 libadwaita webkitgtk-6.0 libsoup3 libsecret mpv yt-dlp"
      ;;
  esac

  if [ -z "${PKG_MGR}" ]; then
    echo "No supported package manager detected for this distribution."
    manual_prereq_message
    confirm_continue && return 0
    return 1
  fi

  pkg_install() {
    case "${PKG_MGR}" in
      pacman)
        run_priv pacman -Sy --needed --noconfirm -- ${PKGS}
        ;;
      apt)
        run_priv apt-get update -y && run_priv apt-get install -y ${PKGS}
        ;;
      dnf)
        run_priv dnf install -y ${PKGS}
        ;;
      zypper)
        run_priv zypper --non-interactive install ${PKGS}
        ;;
      apk)
        run_priv apk add ${PKGS}
        ;;
    esac
  }

  if ! pkg_install; then
    echo "Warning: package installation failed (connection issue, unknown package name, or no sudo/doas available)." >&2
    manual_prereq_message
    confirm_continue && return 0
    return 1
  fi

  # Re-check after installation; fall back to manual instructions if anything
  # is still missing.
  missing=""
  for cmd in git clang blueprint-compiler glib-compile-resources; do
    command -v "$cmd" >/dev/null 2>&1 || missing="$missing $cmd"
  done
  if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
    missing="$missing dotnet-sdk-10"
  fi

  if [ -n "${missing}" ]; then
    echo "Warning: still missing after package installation:${missing}" >&2
    manual_prereq_message
    if ! confirm_continue; then
      return 1
    fi
  fi

  return 0
}

# --------------------------------------------------------------------------
# Source detection (cloned repo vs standalone)
# --------------------------------------------------------------------------

if [ -n "${REPO_ROOT}" ]; then
  echo "Detected SilverScreen repository at ${REPO_ROOT}; building from the existing checkout."
else
  echo "Standalone mode: no SilverScreen checkout detected next to install.sh."
fi

# Install prerequisites before cloning/building (git is needed for the clone).
if [ "${SKIP_DEPS}" -eq 0 ]; then
  if ! ensure_prereqs; then
    echo "Error: prerequisites are not satisfied." >&2
    exit 1
  fi
else
  echo "Skipping prerequisite installation (--skip-deps)."
fi

if [ -n "${REPO_ROOT}" ]; then
  if [ -z "$(ls -A "${REPO_ROOT}/lib/YoutubeAPI" 2>/dev/null)" ]; then
    echo "Initializing git submodules (required to build)..."
    if ! git -C "${REPO_ROOT}" submodule update --init --recursive; then
      echo "Error: failed to initialize submodules. Run 'git submodule update --init --recursive' in ${REPO_ROOT} and retry." >&2
      exit 1
    fi
  fi
else
  # Standalone: clone the repository (with submodules) into a temp directory.
  TEMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/silverscreen-build.XXXXXX")"
  urls="${REPO_URL}"
  [ -n "${urls}" ] || urls="${REPO_URLS_DEFAULT}"
  cloned=""
  for url in ${urls}; do
    echo "Cloning ${url}..."
    if git clone --recurse-submodules "${url}" "${TEMP_DIR}/repo" 2>&1; then
      cloned=1
      break
    fi
    echo "Clone from ${url} failed; trying next source..." >&2
  done
  if [ -z "${cloned}" ]; then
    echo "Error: failed to clone SilverScreen. Set --repo-url or REPO_URL to a valid Git URL and retry." >&2
    exit 1
  fi
  REPO_ROOT="${TEMP_DIR}/repo"
fi

# --------------------------------------------------------------------------
# Build
# --------------------------------------------------------------------------

if ! command -v dotnet >/dev/null 2>&1; then
  echo "Error: 'dotnet' not found on PATH. Install the .NET SDK and retry, or re-run without --skip-deps." >&2
  exit 1
fi

if ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  echo "Error: .NET 10 SDK not found (dotnet is present but without a 10.x SDK). Install the .NET 10 SDK." >&2
  exit 1
fi

echo "Building SilverScreen from ${REPO_ROOT} (this can take a while)..."
if ! (cd "${REPO_ROOT}" && dotnet publish src/SilverScreen.App/SilverScreen.App.csproj \
      /p:DebugType=None /p:DebugSymbols=false \
      -o "${REPO_ROOT}/publish"); then
  echo "Error: build failed." >&2
  exit 1
fi

# --------------------------------------------------------------------------
# Install
# --------------------------------------------------------------------------

find_file() {
  for path in "$@"; do
    if [ -f "$path" ]; then
      printf '%s' "$path"
      return 0
    fi
  done
  return 1
}

BIN_SRC="$(find_file \
  "${REPO_ROOT}/publish/SilverScreen" \
  "${SCRIPT_DIR}/bin/SilverScreen" \
  "${SCRIPT_DIR}/SilverScreen" \
  "${SCRIPT_DIR}/publish/SilverScreen" \
  "${SCRIPT_DIR}/publish/linux-x64/SilverScreen" \
  "${SCRIPT_DIR}/../publish/linux-x64/SilverScreen" \
  "${SCRIPT_DIR}/../publish/SilverScreen" || true)"

DESKTOP_SRC="$(find_file \
  "${REPO_ROOT}/packaging/io.github.silverscreen.SilverScreen.desktop" \
  "${SCRIPT_DIR}/share/applications/io.github.silverscreen.SilverScreen.desktop" \
  "${SCRIPT_DIR}/io.github.silverscreen.SilverScreen.desktop" \
  "${SCRIPT_DIR}/packaging/io.github.silverscreen.SilverScreen.desktop" \
  "${SCRIPT_DIR}/../packaging/io.github.silverscreen.SilverScreen.desktop" || true)"

METAINFO_SRC="$(find_file \
  "${REPO_ROOT}/packaging/io.github.silverscreen.SilverScreen.metainfo.xml" \
  "${SCRIPT_DIR}/share/metainfo/io.github.silverscreen.SilverScreen.metainfo.xml" \
  "${SCRIPT_DIR}/io.github.silverscreen.SilverScreen.metainfo.xml" \
  "${SCRIPT_DIR}/packaging/io.github.silverscreen.SilverScreen.metainfo.xml" \
  "${SCRIPT_DIR}/../packaging/io.github.silverscreen.SilverScreen.metainfo.xml" || true)"

ICON_SRC="$(find_file \
  "${REPO_ROOT}/packaging/io.github.silverscreen.SilverScreen.svg" \
  "${REPO_ROOT}/src/SilverScreen.App/Assets/io.github.silverscreen.SilverScreen.svg" \
  "${REPO_ROOT}/src/SilverScreen.App/Assets/silverscreen.svg" \
  "${SCRIPT_DIR}/share/icons/hicolor/scalable/apps/io.github.silverscreen.SilverScreen.svg" \
  "${SCRIPT_DIR}/io.github.silverscreen.SilverScreen.svg" \
  "${SCRIPT_DIR}/packaging/io.github.silverscreen.SilverScreen.svg" \
  "${SCRIPT_DIR}/../packaging/io.github.silverscreen.SilverScreen.svg" \
  "${SCRIPT_DIR}/silverscreen.svg" \
  "${SCRIPT_DIR}/src/SilverScreen.App/Assets/silverscreen.svg" \
  "${SCRIPT_DIR}/../src/SilverScreen.App/Assets/silverscreen.svg" \
  "${SCRIPT_DIR}/src/SilverScreen.App/Assets/io.github.silverscreen.SilverScreen.svg" \
  "${SCRIPT_DIR}/../src/SilverScreen.App/Assets/io.github.silverscreen.SilverScreen.svg" || true)"

LICENSE_SRC="$(find_file \
  "${REPO_ROOT}/LICENSE" \
  "${SCRIPT_DIR}/share/licenses/silverscreen/LICENSE" \
  "${SCRIPT_DIR}/LICENSE" \
  "${SCRIPT_DIR}/../LICENSE" || true)"

DOC_SRC="$(find_file \
  "${REPO_ROOT}/THIRD-PARTY.md" \
  "${SCRIPT_DIR}/share/doc/silverscreen/THIRD-PARTY.md" \
  "${SCRIPT_DIR}/THIRD-PARTY.md" \
  "${SCRIPT_DIR}/../THIRD-PARTY.md" || true)"

DISCORD_LICENSE_SRC="$(find_file \
  "${REPO_ROOT}/lib/Hi3Helper.SharpDiscordRPC/LICENSE" \
  "${SCRIPT_DIR}/share/licenses/silverscreen/DiscordRPC.LICENSE" \
  "${SCRIPT_DIR}/lib/Hi3Helper.SharpDiscordRPC/LICENSE" \
  "${SCRIPT_DIR}/../lib/Hi3Helper.SharpDiscordRPC/LICENSE" || true)"

YOUTUBE_LICENSE_SRC="$(find_file \
  "${REPO_ROOT}/lib/YoutubeAPI/LICENSE" \
  "${SCRIPT_DIR}/share/licenses/silverscreen/YoutubeAPI.LICENSE" \
  "${SCRIPT_DIR}/lib/YoutubeAPI/LICENSE" \
  "${SCRIPT_DIR}/../lib/YoutubeAPI/LICENSE" || true)"

if [ -z "${BIN_SRC}" ]; then
  echo "Error: SilverScreen executable not found in ${SCRIPT_DIR}." >&2
  exit 1
fi

if [ -z "${DESKTOP_SRC}" ]; then
  echo "Error: Desktop file not found in ${SCRIPT_DIR}." >&2
  exit 1
fi

if [ -z "${METAINFO_SRC}" ]; then
  echo "Error: Metainfo file not found in ${SCRIPT_DIR}." >&2
  exit 1
fi

if [ -z "${ICON_SRC}" ]; then
  echo "Error: Icon file not found in ${SCRIPT_DIR}." >&2
  exit 1
fi

echo "Installing SilverScreen to ${DESTDIR}${PREFIX}..."

if ! mkdir -p "${TARGET_BIN_DIR}" 2>/dev/null; then
  echo "Error: cannot create ${TARGET_BIN_DIR}. Re-run with root privileges (sudo) or use --user." >&2
  exit 1
fi
mkdir -p "${TARGET_APP_DIR}"
mkdir -p "${TARGET_META_DIR}"
mkdir -p "${TARGET_ICON_DIR}"

install -m 755 "${BIN_SRC}" "${TARGET_BIN_DIR}/SilverScreen"
if [ -n "${DISCORD_LICENSE_SRC}" ]; then
  mkdir -p "${TARGET_LIC_DIR}"
  install -m 644 "${DISCORD_LICENSE_SRC}" "${TARGET_LIC_DIR}/DiscordRPC.LICENSE"
fi

if [ -n "${YOUTUBE_LICENSE_SRC}" ]; then
  mkdir -p "${TARGET_LIC_DIR}"
  install -m 644 "${YOUTUBE_LICENSE_SRC}" "${TARGET_LIC_DIR}/YoutubeAPI.LICENSE"
fi
ln -sf "SilverScreen" "${TARGET_BIN_DIR}/silverscreen"

install -m 644 "${DESKTOP_SRC}" "${TARGET_APP_DIR}/io.github.silverscreen.SilverScreen.desktop"
install -m 644 "${METAINFO_SRC}" "${TARGET_META_DIR}/io.github.silverscreen.SilverScreen.metainfo.xml"
install -m 644 "${ICON_SRC}" "${TARGET_ICON_DIR}/io.github.silverscreen.SilverScreen.svg"

if [ -n "${LICENSE_SRC}" ]; then
  mkdir -p "${TARGET_LIC_DIR}"
  install -m 644 "${LICENSE_SRC}" "${TARGET_LIC_DIR}/LICENSE"
fi

if [ -n "${DOC_SRC}" ]; then
  mkdir -p "${TARGET_DOC_DIR}"
  install -m 644 "${DOC_SRC}" "${TARGET_DOC_DIR}/THIRD-PARTY.md"
fi

update_caches

echo "SilverScreen installed successfully to ${DESTDIR}${PREFIX}."
