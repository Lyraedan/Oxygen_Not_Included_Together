# Setup guide — ONI Together Dedicated Server

This guide is intentionally thorough; in practice writing docs is harder than the code.

## What this is

A standalone .NET 8 process. **Current relay model:** it does NOT simulate ONI. The first player to connect (the **master**) runs the colony; the others join it through the server. See `docs/RELAY.md`, including how to give the server its first colony. (Headless authoritative server is a separate future milestone.) You still need ONI + the ONI Together mod on at least one client that will be master.

## Requirements

- Windows/macOS/Linux x64 (mac builds need a Mac; see MACOS.md)
- No ONI installation required for **this process** in current relay mode (but a master client needs ONI + mod); `OniRuntimeDetector` reports if an ONI runtime exists for the future headless milestone
- UDP port (default 7777) open/reachable; LiteNetLib uses UDP

## 1. Get a build

- Download separate release artifact (Windows `.exe`, macOS `.app`, Linux `.AppImage`/self-contained) **or** `dotnet publish` self-contained. Artifacts are meant to live **outside** the mod folder so mod updates can't remove them.

## 2. Choose persistent directories

Pick a data folder, e.g. `ONIServer/data`. Set:
- `ONI_DATA_DIR` (or `--config-dir`) → config root
- `ONI_SERVER_CONFIG_DIR` (or `--config-dir`) → where `multiplayer_settings.json` lives
- `ONI_SERVER_SAVE_DIR` (or `--save-dir`) → saves

Never store these inside the mod directory.

## 3. Ports & networking

- Default **UDP 7777**. `ONI_PORT` / `--port`. Open in firewall + router; forward UDP to server. `ONI_IP=0.0.0.0` binds all; use `127.0.0.1` for local-only.
- LAN discovery uses unconnected UDP on same port (`ONI_DISCOVERY_REQ` → `ONI_DISCOVERY_RESP`); can be disabled `ONI_DISCOVERY_ENABLED=false`; optional `ONI_DISCOVERY_TOKEN`.
- No TCP required for current relay. (Docker maps `7777/udp`.)

## 4. Saves

- `ONI_SAVE_FILE` / `--save`: initial `.sav`. Relative names resolve inside saves dir; absolute paths rejected unless `ONI_ALLOW_EXTERNAL_SAVE_PATHS=true`.
- Server-owned `current.sav` + `backups/`; atomic writes; `ONI_SAVE_BACKUP_COUNT`, `ONI_SAVE_AUTOSAVE_SECONDS`, `ONI_MAX_SAVE_BYTES`, `ONI_SAVE_FILE_NAME`.
- Console: `save`, `saves`, `backup`.
- The master uploads each of its game saves, which replaces `current.sav`. The stored colony wins over `--save` after the first start. Progress since the last game save is only on the master's machine.

## 5. Configuration

`multiplayer_settings.json` (auto-created) + CLI + env. `Sanitize()` clamps unsafe values. See `docs/SECURITY.md`.

## 6. Logs & troubleshooting

- Console output. `ONI_VERBOSE_PACKETS=true` logs packet types/sizes. `ONI_LOG_TO_FILE`, `ONI_LOG_LEVEL`.
- `status`, `players`, `health-check`.
- Common issues: port already in use; firewall blocking UDP; config path not writable; server has no colony yet; mod version or DLC mismatch (see the server log); the master leaving ends the session for everyone.

## 7. Security

See `docs/SECURITY.md`. Non-root in Docker; sandboxed paths; size caps; rate limiting; DLC policy; no secrets in repo.
