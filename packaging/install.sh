#!/bin/sh
set -eu

# install.sh - Installer for SilverScreen
# Supports standard installation prefixes and release tarball layout.

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
  -h, --help          Show this help message

Environment variables:
  PREFIX              Target prefix (default: /usr/local)
  DESTDIR             Staging root directory for packaging
EOF
  exit 0
}

USER_MODE=0
UNINSTALL=0
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
      shift 2
      ;;
    -p)
      [ $# -gt 1 ] || { echo "Error: -p requires an argument" >&2; exit 1; }
      PREFIX="$2"
      shift 2
      ;;
    --prefix=*)
      PREFIX="${1#*=}"
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
  echo "Uninstalling SilverScreen from ${DESTDIR}${PREFIX}..."
  rm -f "${TARGET_BIN_DIR}/SilverScreen"
  rm -f "${TARGET_BIN_DIR}/silverscreen"
  rm -f "${TARGET_APP_DIR}/io.github.silverscreen.SilverScreen.desktop"
  rm -f "${TARGET_META_DIR}/io.github.silverscreen.SilverScreen.metainfo.xml"
  rm -f "${TARGET_ICON_DIR}/io.github.silverscreen.SilverScreen.svg"
  rm -f "${TARGET_LIC_DIR}/LICENSE"
  rmdir "${TARGET_LIC_DIR}" 2>/dev/null || true
  rm -f "${TARGET_DOC_DIR}/THIRD-PARTY.md"
  rmdir "${TARGET_DOC_DIR}" 2>/dev/null || true
  update_caches
  echo "SilverScreen uninstalled successfully."
  exit 0
fi

# Locate source directory and files
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

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
  "${SCRIPT_DIR}/bin/SilverScreen" \
  "${SCRIPT_DIR}/SilverScreen" \
  "${SCRIPT_DIR}/publish/SilverScreen" \
  "${SCRIPT_DIR}/publish/linux-x64/SilverScreen" \
  "${SCRIPT_DIR}/../publish/linux-x64/SilverScreen" \
  "${SCRIPT_DIR}/../publish/SilverScreen" || true)"

DESKTOP_SRC="$(find_file \
  "${SCRIPT_DIR}/share/applications/io.github.silverscreen.SilverScreen.desktop" \
  "${SCRIPT_DIR}/io.github.silverscreen.SilverScreen.desktop" \
  "${SCRIPT_DIR}/packaging/io.github.silverscreen.SilverScreen.desktop" \
  "${SCRIPT_DIR}/../packaging/io.github.silverscreen.SilverScreen.desktop" || true)"

METAINFO_SRC="$(find_file \
  "${SCRIPT_DIR}/share/metainfo/io.github.silverscreen.SilverScreen.metainfo.xml" \
  "${SCRIPT_DIR}/io.github.silverscreen.SilverScreen.metainfo.xml" \
  "${SCRIPT_DIR}/packaging/io.github.silverscreen.SilverScreen.metainfo.xml" \
  "${SCRIPT_DIR}/../packaging/io.github.silverscreen.SilverScreen.metainfo.xml" || true)"

ICON_SRC="$(find_file \
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
  "${SCRIPT_DIR}/share/licenses/silverscreen/LICENSE" \
  "${SCRIPT_DIR}/LICENSE" \
  "${SCRIPT_DIR}/../LICENSE" || true)"

DOC_SRC="$(find_file \
  "${SCRIPT_DIR}/share/doc/silverscreen/THIRD-PARTY.md" \
  "${SCRIPT_DIR}/THIRD-PARTY.md" \
  "${SCRIPT_DIR}/../THIRD-PARTY.md" || true)"

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

mkdir -p "${TARGET_BIN_DIR}"
mkdir -p "${TARGET_APP_DIR}"
mkdir -p "${TARGET_META_DIR}"
mkdir -p "${TARGET_ICON_DIR}"

install -m 755 "${BIN_SRC}" "${TARGET_BIN_DIR}/SilverScreen"
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
