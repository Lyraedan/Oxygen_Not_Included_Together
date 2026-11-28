# ONI Together Dedicated Server

.NET 8 standalone **LiteNetLib 1.3.1 relay** server for ONI Together. It is **not yet a headless, server-authoritative ONI simulation**; the first player to connect (the **master**) runs the game simulation. How it works and how to start: `docs/RELAY.md`. See also `TODO.md` at repo root (final milestone) and `docs/SECURITY.md`.

## Build

```sh
dotnet build ONI_Together_DedicatedServer/ONI_Together_DedicatedServer.csproj -c Release
```

## Test

```sh
dotnet test ONI_Together_DedicatedServer.Tests/ONI_Together_DedicatedServer.Tests.csproj -c Release
```

## Run

```sh
dotnet run --project ONI_Together_DedicatedServer -- --non-interactive --port 7777
```

or a published self-contained binary. See platform guides:

- Windows: `docs/WINDOWS.md`, launcher `packaging/windows/ONI_Together_DedicatedServer.bat`
- Linux: `docs/LINUX.md`, launcher `packaging/linux/ONI_Together_DedicatedServer.sh`, `packaging/linux/make-appimage.sh`
- macOS: `docs/MACOS.md`, launcher `packaging/macos/ONI_Together_DedicatedServer.command`, `packaging/macos/make-app.sh`
- Docker/ZimaOS: `docs/DOCKER.md`, `Dockerfile`, `docker-compose.yml`
- Security: `docs/SECURITY.md`
- Full setup: `docs/SETUP.md`

## Publish (self-contained)

```powershell
# Windows
./scripts/publish-windows.ps1
```
```sh
# Linux (cross-publish works)
sh ./scripts/publish-linux.sh Release
```
```sh
# macOS (run on a Mac; builds not available on Windows/Linux)
bash ./scripts/publish-macos.command osx-arm64 Release
```

## CLI

```
--config-dir <path>   persistent config/data dir (alias --data-dir)
--save-dir <path>     saves dir
--ip <address>        bind IP (default 0.0.0.0)
--port <n>            UDP port (default 7777, 1-65535)
--max-players <n>     lobby size (1-1000)
--tick-rate <hz>      10-240 (default 60)
--save <name|path>    initial .sav (sandboxed to saves dir unless ONI_ALLOW_EXTERNAL_SAVE_PATHS=true)
--server-name <name>
--non-interactive / --headless
--verbose
--health-check        print 'ok', exit 0
--version
-h / --help
```

## Console commands

`help`, `quit`, `stop`, `players`/`listplayers`, `status`, `whatis`, `reloadconfig`, `save`, `saves`, `backup`.

## Persistent data

Config `multiplayer_settings.json` + `saves/` are **outside** the app/mod directory. Defaults per-OS app-data; override with `ONI_DATA_DIR` / `ONI_SERVER_CONFIG_DIR` / `ONI_SERVER_SAVE_DIR`. Mod updates cannot wipe these. Saves are written **atomically** with rotated backups; server-owned `current.sav` + `backups/`.

## Environment overrides

See `docs/SETUP.md` and `TODO.md`. Container-friendly names include `ONI_*`.
