#!/usr/bin/env sh
# Build an AppImage from a self-contained `linux-x64` publish output.
# Requires `appimagetool` on PATH (see https://appimage.github.io/appimagetool/). Run on Linux.
set -eu

PUBLISH_DIR="${1:?usage: make-appimage.sh <publish-dir> [out-dir]}"
OUT_DIR="${2:-.}"
APPDIR="$OUT_DIR/ONI_Together_DedicatedServer.AppDir"

rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin"

cp -R "$PUBLISH_DIR/." "$APPDIR/usr/bin/"
cp "$APPDIR/usr/bin/ONI_Together_DedicatedServer" "$APPDIR/AppRun" 2>/dev/null || cat > "$APPDIR/AppRun" <<'SH'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/ONI_Together_DedicatedServer" "$@"
SH
chmod +x "$APPDIR/AppRun"

cat > "$APPDIR/ONI_Together_DedicatedServer.desktop" <<'DESK'
[Desktop Entry]
Type=Application
Name=ONI Together Dedicated Server
Exec=ONI_Together_DedicatedServer
Icon=dedi-icon
Categories=Game;Network;
Terminal=true
DESK

# Icon (184x184 palette PNG shipped with the project).
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cp "$SCRIPT_DIR/../../Assets/dedi-icon.png" "$APPDIR/dedi-icon.png" 2>/dev/null || true

if command -v appimagetool >/dev/null 2>&1; then
    appimagetool "$APPDIR" "$OUT_DIR/ONI_Together_DedicatedServer.AppImage"
    echo "Created $OUT_DIR/ONI_Together_DedicatedServer.AppImage"
else
    echo "appimagetool not found; AppDir prepared at $APPDIR" >&2
    echo "Install appimagetool and run: appimagetool $APPDIR $OUT_DIR/ONI_Together_DedicatedServer.AppImage" >&2
    exit 1
fi
