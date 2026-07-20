#!/bin/sh
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
BUILD="$ROOT/ONI_Together/bin/Release/netstandard2.1"
STAGE="$ROOT/dist/ONI_Together-workshop"
PREVIEW="$ROOT/dist/ONI_Together-workshop-preview.png"

for file in ONI_Together.dll mod.yaml mod_info.yaml; do
	test -f "$BUILD/$file" || { echo "Missing Release build file: $BUILD/$file" >&2; exit 1; }
done

rm -rf "$STAGE"
mkdir -p "$STAGE"
cp "$BUILD/ONI_Together.dll" "$BUILD/mod.yaml" "$BUILD/mod_info.yaml" "$STAGE/"
cp -R "$ROOT/ONI_Together/ModAssets/assets" "$STAGE/assets"
cp -R "$ROOT/ONI_Together/ModAssets/translations" "$STAGE/translations"
cp "$ROOT/LICENSE.md" "$ROOT/THIRD_PARTY_NOTICES.md" "$STAGE/"
cp "$ROOT/ONI_Together/Assets/mod_mascot.png" "$PREVIEW"

for platform in windows mac linux; do
	test -s "$STAGE/assets/$platform/oni_mp_ui_assets" || { echo "Missing $platform asset bundle" >&2; exit 1; }
done
grep -qx 'staticID: ONI_Together' "$STAGE/mod.yaml"
grep -qx 'version: 1.0.2' "$STAGE/mod_info.yaml"
grep -qx 'APIVersion: 2' "$STAGE/mod_info.yaml"
test "$(od -An -t x1 -N3 "$STAGE/mod_info.yaml" | tr -d ' ')" != efbbbf
test "$(find "$STAGE" -type f \( -name '*.pdb' -o -name '*.zip' \) -print -quit)" = ""
test "$(wc -c < "$PREVIEW")" -lt 1048576

echo "Workshop content: $STAGE"
echo "Preview image:    $PREVIEW"
find "$STAGE" -type f -print | LC_ALL=C sort
shasum -a 256 "$STAGE/ONI_Together.dll"
