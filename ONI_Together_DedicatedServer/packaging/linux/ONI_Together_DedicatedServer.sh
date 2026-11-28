#!/usr/bin/env sh
# ONI Together Dedicated Server - Linux launcher
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
ONI_SERVER_EXE=${ONI_SERVER_EXE:-"$SCRIPT_DIR/ONI_Together_DedicatedServer"}

if [ ! -x "$ONI_SERVER_EXE" ] && [ ! -f "$ONI_SERVER_EXE" ]; then
    echo "ERROR: Could not find server executable:" >&2
    echo "  $ONI_SERVER_EXE" >&2
    echo "Set ONI_SERVER_EXE or place this launcher next to ONI_Together_DedicatedServer." >&2
    exit 1
fi

echo "Starting ONI Together Dedicated Server..."
echo "  exe:  $ONI_SERVER_EXE"
[ -n "${ONI_DATA_DIR:-}" ] && echo "  data: $ONI_DATA_DIR"

# If exe isn't executable, try chmod.
if [ ! -x "$ONI_SERVER_EXE" ]; then chmod +x "$ONI_SERVER_EXE" 2>/dev/null || true; fi

exec "$ONI_SERVER_EXE" "$@"
