#!/usr/bin/env sh
# Publish self-contained linux-x64 dedicated server.
set -eu

CONFIG="${1:-Release}"
OUT="${2:-$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)/artifacts/linux-x64}"
PROJ="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)/ONI_Together_DedicatedServer.csproj"
PKGDIR="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)/packaging"

rm -rf "$OUT"
mkdir -p "$OUT"

dotnet publish "$PROJ" \
    -c "$CONFIG" \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:PublishTrimmed=false \
    -o "$OUT"

cp "$PKGDIR/linux/ONI_Together_DedicatedServer.sh" "$OUT/"
cp "$PKGDIR/linux/make-appimage.sh" "$OUT/" 2>/dev/null || true
chmod +x "$OUT/ONI_Together_DedicatedServer" "$OUT/ONI_Together_DedicatedServer.sh" "$OUT/make-appimage.sh" 2>/dev/null || true
echo "linux-x64 published to $OUT"
