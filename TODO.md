# Dedicated Server TODO (`ONI_Together_DedicatedServer`)

> **Owner:** Malionaro (PRs & Reviews with ItsLuke)  
> **Target Branch:** `0.8.1`  
> **Current State:** Experimental `.NET 8.0` relay/listening server (`ONI_Together_DedicatedServer/`) using `RiptideNetworking.Riptide` (v2.2.1). No headless ONI simulation yet; the first connected client acts as `master` and owns the authoritative save state.

---

## 🎯 Platform & Release Priority

1. **Windows** — Standalone `.exe` (self-contained) + `.bat` launcher
2. **Linux** — `.AppImage` + `.sh` launcher (plus `systemd` service example)
3. **Docker** — `Dockerfile` + `docker-compose.yml` (ZimaOS-compatible, persistent volumes, env-var config)
4. **macOS** — `.app` + `.command` launcher *(last; ItsLuke will handle the macOS build)*

> **Distribution:** Shipped as separate downloadable artifacts via GitHub Releases (not bundled inside the mod directory, since mod updates nuke the mod folder).

---

## 1. Core Architecture & Transport

- [ ] **Migrate from Riptide to `LiteNetLib` / `ITransport`**
  - Align `ONI_Together_DedicatedServer` with the `0.8.1` mod transport layer (`LiteNetLib` 1.3.1 / `ITransport`) to remove Riptide's 1000-byte packet limit.
- [ ] **Externalize Config & Save Paths**
  - Move `dedicated_server_config/` and save files out of `AppContext.BaseDirectory` into a persistent user/OS data directory (e.g. `%APPDATA%/ONI_Together_DedicatedServer` on Windows, `~/.config/oni-together-server` on Linux, `/data` in Docker) or configurable via `--config-dir` / env var, so updates never wipe configs or saves.
- [ ] **Non-Interactive / Headless Mode**
  - Remove blocking `Console.ReadLine()` on shutdown when running in headless/Docker/service environments (`--non-interactive` or `Console.IsInputRedirected` check) and handle `SIGINT` / `SIGTERM` gracefully.
- [ ] **Tick Rate & CPU Throttling**
  - Add a configurable server tick rate / sleep interval in the main loop so the relay doesn't spin at 100% CPU when idle.

---

## 2. Save File & Master-Client Lifecycle

- [ ] **In-Memory Save + Periodic Auto-Save**
  - Keep the active save in memory, flush to disk on a configurable interval, and always save on graceful shutdown.
- [ ] **Master Migration & Handshake**
  - Verify clean master handoff when the current master disconnects while other clients are connected (next client uploads/confirms authoritative state).
- [ ] **Pause When Empty**
  - Option to pause simulation/relay state when 0 players are connected (standard in Factorio/Minecraft/Valheim servers).
- [ ] **Cycle-Based Hard Sync Option**
  - Support server-side configuration for periodic hard-sync intervals (e.g. every N cycles).

---

## 3. Standard Dedicated Server Features (Parity with Other Games)

- [ ] **Authentication & Access Control**
  - Server password protection (`Password` in config)
  - Whitelist / Allowlist & Banlist (by SteamID / player ID, persisted in JSON)
  - Max player limit (`MaxPlayers`)
- [ ] **Server Browser & Discovery**
  - LAN UDP broadcast discovery support
  - Server name, MOTD (Message of the Day), and version/mod-version compatibility check on join
- [ ] **Admin & Console Commands**
  - `players` / `status` (list connected players, ping, master indicator)
  - `kick <player>` / `ban <player>` / `unban <player>`
  - `say <message>` (server broadcast to all clients)
  - `save` (trigger immediate save/backup)
  - `reload` (reload `multiplayer_settings.json` without restart)
  - `stop` (graceful shutdown with save + client notification)
- [ ] **Logging & Diagnostics**
  - Structured file logging with log rotation (not just stdout)
  - Configurable log level (`Info`, `Warn`, `Debug`)

---

## 4. Packaging, Scripts & Docker

- [ ] **Windows (Phase 1)**
  - `dotnet publish -c Release -r win-x64 --self-contained` single-file `.exe`
  - `start_server.bat` launcher with default flags & restart-on-crash option
- [ ] **Linux (Phase 2)**
  - `dotnet publish -c Release -r linux-x64 --self-contained`
  - Package as `.AppImage` + `start_server.sh`
- [ ] **Docker / ZimaOS (Phase 3)**
  - Multi-stage `Dockerfile` (`.NET 8.0` SDK build -> minimal runtime image)
  - `docker-compose.yml` with `/data` volume mount for config & saves, UDP/TCP port mapping, and environment variable overrides (`ONI_PORT`, `ONI_PASSWORD`, `ONI_MAX_PLAYERS`, etc.)
  - `HEALTHCHECK` instruction
- [ ] **macOS (Phase 4)**
  - Coordinate with ItsLuke for `.app` bundle + `.command` launcher
