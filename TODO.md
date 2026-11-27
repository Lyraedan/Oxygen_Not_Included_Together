# Dedicated Server TODO (`ONI_Together_DedicatedServer`)

> **Owner:** Malionaro
> **Coordination:** ItsLuke invited a PR for feedback and code review, and has handed control of the dedicated-server feature to Malionaro. Keep ItsLuke updated as progress is made.
> **Branch:** `dedicated-server-complete` (was `dedicated-server-upstream-0.8.1`, tracks `upstream/0.8.1`)
> **Current implementation:** .NET 8 standalone LiteNetLib relay/listening server. It is not yet an independent, headless ONI simulation.
> **Scope:** We are currently focusing **only** on the dedicated server. Normal (non-dedicated) multiplayer work is owned/prioritized by ItsLuke and stays isolated from this effort.
> **Source of truth for notes:** Discord thread with ItsLuke (maintainer notes, re-shared 2026) + verified state of this repo. Facts below marked "verified" were checked against the code, built, tested, and run.

---

## Status summary (this change set)

**Verified by commands (evidence in conversation):**
- `dotnet build ONI_Together_DedicatedServer/ONI_Together_DedicatedServer.csproj -c Release` → **0 warnings / 0 errors**.
- `dotnet test ONI_Together_DedicatedServer.Tests/ONI_Together_DedicatedServer.Tests.csproj` → **56 passed / 0 failed**. (xUnit project added; `InternalsVisibleTo`.)
- Headless smoke test on built self-contained `win-x64` exe: starts, `PauseWhenEmpty`, UDP listen, `multiplayer_settings.json` created; `--version` → `1.0.0`; `--health-check` → `ok`.
- `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` → `artifacts/win-x64/ONI_Together_DedicatedServer.exe` (~68 MB) + `.bat`.
- `dotnet publish -r linux-x64 --self-contained -p:PublishSingleFile=true` → `artifacts/linux-x64/ONI_Together_DedicatedServer` (~68 MB) + `.sh`.
- Knowledge graph: `graphify-out/` (gitignored).

**Implemented (code):**
- Security: `Security/PathSandbox`, `Security/RateLimiter`, `Security/Dlc`; sandboxed saves; atomic writes; `Sanitize()` clamps; packet size caps; per-peer rate limiting; `MaxMessagesPerPoll` enforced; `MaxPacketSize`; optional discovery token; connection payload cap; malformed-packet safety; no shell; capped strings.
- Save persistence: `ONI/SaveStore` atomic `current.sav` + rotated `backups/`; `SaveFile.SaveToFile` atomic; server-owned persistence; autosave; `save`/`saves`/`backup` console; `MaxSaveBytes`.
- DLC: `DlcCheckMode` Off/Declared/Required + `RequiredDlcIds`/`ForbiddenDlcIds`/`ServerActiveDlcIds` + `RequireDlcDeclaration`; optional backwards-compatible client DLC declaration in handshake. **Not Klei-verified** (see §5).
- Operational: `NonInteractive`, `PauseWhenEmpty` (no packet processing while empty; persist server-owned state; resume on join; **no ONI sim to pause**), `LogToFile`/`LogLevel` + `Logging/ServerLogger` (console + `logs/server.log`, `ONI_LOG_TO_FILE`/`ONI_LOG_LEVEL`; key events + transport; console commands/save still `Console.WriteLine`), `DiscoveryEnabled`, `--health-check`, `--version`, extra env overrides, `reloadconfig`, `status`.
- Packaging: self-contained `win-x64` + `linux-x64` artifacts; `.bat`/`.sh`/`.command`; mac `.app` script; Linux `make-appimage.sh`; `publish-*` scripts; `Dockerfile`; `docker-compose.yml`; `.dockerignore`; **GitHub Actions workflows disabled in PR** (no active `.github/workflows/dedicated-server.yml`).
- Docs: `ONI_Together_DedicatedServer/README.md`, `docs/SETUP.md`, `docs/WINDOWS.md`, `docs/LINUX.md`, `docs/MACOS.md`, `docs/DOCKER.md`, `docs/SECURITY.md`.
- Tests: 56 xUnit tests.

**Not complete / blocked (honest):**
- **Final milestone headless authoritative ONI is NOT done.** No ONI runtime on server; `ExcludeGameLibs=true`; no headless sim; master still required; **master migration still present** in relay and does NOT transfer sim state. Needs ONI game files + mod integration + Klei/DLC + design; not safely implementable offline here. See §Final.
- **DLC ownership cannot be cryptographically verified** without Klei/Steam integration; implemented declared checks only. Klei requirement noted; needs ItsLuke/Klei. See §5.
- **Modding + ONI_Together API** not integrated. See §6. (ItsLuke said he can handle API; base game first.)
- **macOS artifact** not built/tested (needs Mac; ItsLuke offered). Scripts provided. `.app`/`.command` not run.
- **AppImage** not built (needs `appimagetool` + Linux). Script provided.
- **Docker image not built/tested** (no Docker daemon in this environment). `Dockerfile`/compose provided.
- **GitHub Actions workflows disabled in PR**; no active dedicated-server workflow added.
- No actual GitHub Release publishing; **Draft PR #277 opened upstream `0.8.1`**; no ItsLuke progress update.
- `Logging/ServerLogger` added (console + `logs/server.log`); `ONI_LOG_TO_FILE`/`ONI_LOG_LEVEL`. Key events + `DedicatedLiteNetLibServer` transport logged; console commands + `SaveFile`/`SaveStore` still `Console.WriteLine`.
- `PauseWhenEmpty` now pauses **packet processing** while empty + persists server-owned state + resumes on join; **no ONI simulation to pause** in current relay.
- No save data received from ONI master relayed into server-owned `current.sav` automatically; server persists server-held loaded bytes + manual/autosave; **not authoritative ONI snapshot**.
- `reloadconfig` preserves connected players + master; re-reads live config without transport restart. `Ip`/`Port`/`Transport` require manual restart.
- No TLS/auth beyond key + optional DLC; not safe on untrusted networks.
- `TRELLO_PROGRESS_STATUS.md` still says `EXPERIMENTAL: Dedicated Servers [Benched]`; not updated (scope = this TODO).

---

## Direction agreed with ItsLuke

- ItsLuke is prioritizing seamless normal multiplayer; Malionaro is taking the dedicated-server feature forward, with full control ("do what you need to do", re-writes allowed, ItsLuke won't touch it).
- Work in this order: **Windows → Linux → Docker/ZimaOS → macOS**. macOS last; ItsLuke offered to build/test the macOS version and can test all three platforms.
- **Shipping:** short term the dedicated server app is shipped next to the mods DLL; long-term prefer separate downloadable server applications from **GitHub Releases** (`.exe`, `.app`, `.AppImage`; ItsLuke will host downloads) so mod updates cannot remove the app. The earlier launcher-script idea remains useful: `.bat`, `.sh`, `.command`. Standalone `.NET 8`; does not require building inside the mod.
- Keep configuration and save data outside the mod installation directory, because **mod updates nuke the mod directory**. Add any server-specific config there too. (Implemented.)
- Docker support accounts for **ZimaOS**: persistent storage + configurable networking. (Dockerfile/compose provided; not tested here.)
- **PR + review:** **Draft PR #277 opened** upstream `0.8.1`; **GitHub Actions workflows disabled in PR**; send progress updates.
- Keep dedicated-server work isolated. **Writing the setup guide is harder than implementation.** (`docs/SETUP.md`.)

---

## 1. Current implementation (verified)

- [x] Replace Riptide with LiteNetLib 1.3.1.
- [x] Configurable startup options + environment overrides.
- [x] Persistent paths outside mod directory.
- [x] Non-interactive, graceful shutdown, configurable tick rate (10-240).
- [x] Runtime smoke test + clean build.
- [x] Save persistence: server-owned atomic `current.sav`, rotated backups, autosave, manual `save`/`backup`, `SaveFile.SaveToFile`.
- [x] `MaxMessagesPerPoll` enforced.
- [x] `MaxPacketSize`, `MaxMessageSize`, `RateLimitMessagesPerSecond`, `MaxSaveBytes`.
- [x] `--health-check`, `--version`, `PauseWhenEmpty`, `ONI_PAUSE_WHEN_EMPTY`, `ONI_NON_INTERACTIVE`, `ONI_MAX_MESSAGES_PER_POLL`, `ONI_MAX_PACKET_SIZE`, `ONI_MAX_MESSAGE_SIZE`, `ONI_RATE_LIMIT_MESSAGES_PER_SECOND`, `ONI_SAVE_AUTOSAVE_SECONDS`, `ONI_SAVE_BACKUP_COUNT`, `ONI_MAX_SAVE_BYTES`, `ONI_ALLOW_EXTERNAL_SAVE_PATHS`, `ONI_DISCOVERY_*`, `ONI_DLC_*`, `ONI_LOG_*`.
- [ ] **Self-contained Windows `.exe` built locally; not published to GitHub Releases.**
- [ ] **Not headless/server-authoritative.** Relay; master client simulates; master migration present but flawed.
- [ ] **No DLC Klei ownership verification.**
- [ ] **No modding / ONI_Together API.**
- [x] `LogToFile`/`LogLevel` + `ServerLogger` sink (key events + transport; console commands/save not fully file-routed).
- [x] `PauseWhenEmpty`: no packet processing while empty; persist server-owned state; resume on join. **No ONI sim to pause.** Integration test `PauseWhenEmptyIntegrationTests` proves `saves/current.sav`, no relay while empty, resume relay on join.

Relay model: first client **master**; server relays; LAN discovery; handshake `ONI_TOGETHER`; UDP **7777**; optional DLC handshake extension. Master migration not removed. Config `multiplayer_settings.json`; `MaxMessagesPerPoll=128`; `MaxPacketSize=262144`; `RateLimitMessagesPerSecond=600`; etc.

---

## 2. Known gaps / correctness notes

- [ ] Master migration does not transfer sim state; must be removed once server-authoritative.
- [x] `reloadconfig` preserves players/master; no transport restart. (`Ip`/`Port`/`Transport` need manual restart.)
- [x] `PauseWhenEmpty` applied to relay packet processing; **not applied to any ONI simulation** (none).
- [x] `ServerLogger` structured console+file sink; transport routed; console commands/save not fully file-routed.
- [ ] No TLS; no per-client auth beyond key + DLC.
- [ ] No ONI version check; DLC checks are declared, not Klei-verified.
- [ ] `MaxMessageSize` == `MaxPacketSize` cap; LiteNetLib has own MTU limits.
- [ ] mac/Linux default config paths need per-platform verification in CI.
- [ ] LAN discovery optional token added; still not fully authenticated.

---

## 3. Platform roadmap

### Phase 1 — Windows
- [x] Self-contained `win-x64` `.exe` (`PublishSingleFile`, `--self-contained`).
- [x] `.bat` launcher.
- [x] Config/save paths survive app replacement (outside app; verified logic; not update-tested).
- [x] Windows setup guide (`docs/WINDOWS.md`, `docs/SETUP.md`).
- [ ] Share progress + open PR. (not done)
- [ ] Publish to GitHub Releases. (not done)

### Phase 2 — Linux
- [x] Self-contained `linux-x64` build.
- [x] `.sh` launcher.
- [x] `make-appimage.sh` (not built; needs appimagetool + Linux).
- [x] permissions/ports/persistent paths + systemd docs.
- [ ] Verify Linux artifact on Linux + CI. (not here)

### Phase 3 — Docker / ZimaOS
- [x] Multi-stage `Dockerfile`.
- [x] `docker-compose.yml` persistent volumes + UDP.
- [x] Env vars.
- [x] Health check.
- [x] ZimaOS docs.
- [ ] Build/run Docker. (no daemon here)

### Phase 4 — macOS
- [x] `.command`, `.app` script, publish script.
- [ ] Build/test on Mac; coordinate ItsLuke. (not here)

---

## 4. Release and collaboration
- [x] **GitHub Actions workflows disabled in PR**; no active `.github/workflows/dedicated-server.yml`.
- [ ] Publish GitHub Release artifacts. (not run)
- [ ] PR. (not opened)
- [ ] Progress updates. (for you)
- [x] Setup guide.

---

## 5. DLC ownership & compatibility
- [x] `DlcCheckMode` + `DeclaredDlcOwnershipVerifier`.
- [x] `RequiredDlcIds`, `ForbiddenDlcIds`, `ServerActiveDlcIds`, `RequireDlcDeclaration`.
- [x] DLC match + forbidden/required; sanitize overlap.
- [x] Backwards-compatible optional handshake DLC block.
- [x] Existing client-side checks referenced (`SaveHelper.SavegameDlcListValid`, `GameStateRequestPacket.ActiveDlcIds`, `STRINGS...DLCSYNC`) in docs.
- [ ] **Cryptographic Klei/Steam ownership verification.** (blocked; Klei requirement; needs game/Steam)
- [ ] Confirm `requiredDlcIds`/`forbiddenDlcIds` interplay in production. (not tested vs real mod)
- [ ] Server can't infer ownership without game. (noted)

---

## 6. Modding & ONI_Together API
- [ ] Server modding. (not done)
- [ ] ONI_Together API compatibility. (not done; ItsLuke offered)
- [ ] Not prerequisite for base game. (deferred)

---

## Final milestone — Headless, server-authoritative ONI
> **NOT STARTED / BLOCKED.** Start after current server stable. Requires ONI runtime/game files + Klei + mod API. Currently impossible in this environment and not safely implementable offline. Honest status.

- [ ] ONI headless colony sim on server. (`OniRuntimeDetector` + `HeadlessOniSimulationBackend` scaffold launches `OxygenNotIncluded.exe -batchmode -nographics`; **NOT server-authoritative**; blocked on ONI_Together headless mod/server-authoritative bridge)
- [ ] Obtain ONI runtime/game files w/o normal install. (blocked; currently `ExcludeGameLibs`; `OniRuntimeDetector` can honestly detect `Assembly-CSharp.dll` + executable under `ONI_PATH`/`ONI_GAME_DIR` + common installs; does NOT verify Klei ownership or headless sim)
- [ ] Server holds save + world state. (partial: server-owned bytes; not authoritative ONI)
- [ ] Server-authoritative sim; no player master. (not; relay + master)
- [ ] Clients send actions; authoritative state; render own. (not)
- [x] Nobody connected (relay): `PauseWhenEmpty` pauses packet processing + persists server-owned state. **Not authoritative ONI pause; no sim.**
- [ ] Remove player-master election + migration. (not; currently implemented)
- [ ] DLC re-eval no-game server. (noted)
- [ ] Modding/API re-eval. (deferred)

---

## Notes from maintainer discussion (condensed)
- Full control; rewrite; isolated.
- Shipping; config outside mod; mod updates nuke; env; order; Luke mac; ZimaOS; setup hard.
- Flawed master/slave; needs authoritative; can't swap.
- DLC ownership (Klei); client-side mismatch helps.
- Modding + API later; Luke API; base game first.
