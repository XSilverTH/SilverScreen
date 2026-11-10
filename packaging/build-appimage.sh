#!/bin/sh
set -eu

APPDIR="${1:-./AppDir}"
OUTPUT="${2:-SilverScreen-x86_64.AppImage}"
PUBLISH_DIR="${3:-./publish}"

echo "==> Preparing AppDir at ${APPDIR}..."
rm -rf "${APPDIR}"
mkdir -p "${APPDIR}/usr/bin"
mkdir -p "${APPDIR}/usr/lib"
mkdir -p "${APPDIR}/usr/share/applications"
mkdir -p "${APPDIR}/usr/share/icons/hicolor/scalable/apps"
mkdir -p "${APPDIR}/usr/share/icons/hicolor/256x256/apps"
mkdir -p "${APPDIR}/usr/share/metainfo"
mkdir -p "${APPDIR}/usr/share/glib-2.0/schemas"

# Binary
echo "==> Copying application binary..."
cp "${PUBLISH_DIR}/SilverScreen" "${APPDIR}/usr/bin/SilverScreen"
chmod 755 "${APPDIR}/usr/bin/SilverScreen"

# Desktop integration
echo "==> Setting up desktop metadata and icons..."
cp packaging/io.github.silverscreen.SilverScreen.desktop "${APPDIR}/"
cp packaging/io.github.silverscreen.SilverScreen.desktop "${APPDIR}/usr/share/applications/"
cp packaging/io.github.silverscreen.SilverScreen.svg "${APPDIR}/"
cp packaging/io.github.silverscreen.SilverScreen.svg "${APPDIR}/usr/share/icons/hicolor/scalable/apps/"
cp packaging/io.github.silverscreen.SilverScreen.metainfo.xml "${APPDIR}/usr/share/metainfo/"

# Create PNG icon for .DirIcon (appimagetool requires PNG for file managers)
if command -v rsvg-convert >/dev/null 2>&1; then
  rsvg-convert -w 256 -h 256 packaging/io.github.silverscreen.SilverScreen.svg -o "${APPDIR}/io.github.silverscreen.SilverScreen.png"
  cp "${APPDIR}/io.github.silverscreen.SilverScreen.png" "${APPDIR}/usr/share/icons/hicolor/256x256/apps/io.github.silverscreen.SilverScreen.png"
  ln -sf io.github.silverscreen.SilverScreen.png "${APPDIR}/.DirIcon"
else
  ln -sf io.github.silverscreen.SilverScreen.svg "${APPDIR}/.DirIcon"
fi

# AppRun launcher
cp packaging/AppRun "${APPDIR}/AppRun"
chmod 755 "${APPDIR}/AppRun"

# Bundle cutting-edge libraries from build system so non-Arch systems have them
echo "==> Bundling runtime libraries..."
for lib in /usr/lib/libadwaita-1.so* /usr/lib/libministream.so* /usr/lib/libglycin-2.so*; do
  if [ -e "$lib" ]; then
    cp -d "$lib" "${APPDIR}/usr/lib/"
  fi
done

# Compile GSettings schemas if present
glib-compile-schemas "${APPDIR}/usr/share/glib-2.0/schemas/" 2>/dev/null || true

# Locate or download appimagetool
APPIMAGETOOL="${APPIMAGETOOL:-}"
if [ -z "${APPIMAGETOOL}" ]; then
  if command -v appimagetool >/dev/null 2>&1; then
    APPIMAGETOOL="appimagetool"
  elif [ -f "./appimagetool" ]; then
    APPIMAGETOOL="./appimagetool"
  else
    echo "==> Downloading appimagetool..."
    curl -sLo ./appimagetool https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
    chmod +x ./appimagetool
    APPIMAGETOOL="./appimagetool"
  fi
fi

echo "==> Generating AppImage: ${OUTPUT}..."
APPIMAGE_EXTRACT_AND_RUN=1 "${APPIMAGETOOL}" --appimage-extract-and-run "${APPDIR}" "${OUTPUT}"

echo "==> AppImage created successfully: ${OUTPUT}"
