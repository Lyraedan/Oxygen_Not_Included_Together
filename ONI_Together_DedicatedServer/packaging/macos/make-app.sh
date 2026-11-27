#!/usr/bin/env bash
# Build a minimal macOS .app bundle from a self-contained `osx-*` publish output.
# Run on macOS. Requires a published binary at $1.
set -euo pipefail

PUBLISH_DIR="${1:?usage: make-app.sh <publish-dir> [out-dir]}"
OUT_DIR="${2:-.}"
APP="$OUT_DIR/ONI_Together_DedicatedServer.app"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

cp -R "$PUBLISH_DIR/." "$APP/Contents/Resources/"

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>ONI_Together_DedicatedServer</string>
  <key>CFBundleDisplayName</key><string>ONI Together Dedicated Server</string>
  <key>CFBundleIdentifier</key><string>com.oxygennotincludedtogether.dedicatedserver</string>
  <key>CFBundleVersion</key><string>1.0.0</string>
  <key>CFBundleShortVersionString</key><string>1.0.0</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleExecutable</key><string>ONI_Together_DedicatedServer</string>
  <key>LSBackgroundOnly</key><true/>
</dict>
</plist>
PLIST

cat > "$APP/Contents/MacOS/ONI_Together_DedicatedServer" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ONI_DATA_DIR="${ONI_DATA_DIR:-$HOME/Library/Application Support/ONI_Together_DedicatedServer}"
ONI_SERVER_CONFIG_DIR="${ONI_SERVER_CONFIG_DIR:-$ONI_DATA_DIR/config}"
ONI_SERVER_SAVE_DIR="${ONI_SERVER_SAVE_DIR:-$ONI_DATA_DIR/saves}"
mkdir -p "$ONI_SERVER_CONFIG_DIR" "$ONI_SERVER_SAVE_DIR"
exec "$HERE/Resources/ONI_Together_DedicatedServer" \
  --config-dir "$ONI_SERVER_CONFIG_DIR" --save-dir "$ONI_SERVER_SAVE_DIR" "$@"
SH

chmod +x "$APP/Contents/MacOS/ONI_Together_DedicatedServer"
chmod +x "$APP/Contents/Resources/ONI_Together_DedicatedServer" 2>/dev/null || true
echo "Created $APP"
