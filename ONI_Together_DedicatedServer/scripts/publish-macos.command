#!/usr/bin/env bash
# Publish self-contained macOS dedicated server. Run on macOS. Requires ItsLuke (or you on a Mac).
set -euo pipefail

RID="${1:-osx-arm64}"   # osx-arm64 (Apple Silicon) or osx-x64 (Intel)
CONFIG="${2:-Release}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJ="$HERE/../ONI_Together_DedicatedServer.csproj"
OUT="$HERE/../artifacts/$RID"

rm -rf "$OUT"; mkdir -p "$OUT"

dotnet publish "$PROJ" -c "$CONFIG" -r "$RID" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o "$OUT"

cp "$HERE/../packaging/macos/ONI_Together_DedicatedServer.command" "$OUT/"
cp "$HERE/../packaging/macos/make-app.sh" "$OUT/" 2>/dev/null || true
chmod +x "$OUT/ONI_Together_DedicatedServer" "$OUT/ONI_Together_DedicatedServer.command" "$OUT/make-app.sh" 2>/dev/null || true
echo "macOS ($RID) published to $OUT"
