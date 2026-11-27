# Dedicated Server TODO (`ONI_Together_DedicatedServer`)

> **Owner:** Malionaro
> **Coordination:** ItsLuke invited a PR for feedback and code review, and has handed control of the dedicated-server feature to Malionaro. Keep ItsLuke updated as progress is made.
> **Branch:** `dedicated-server-upstream-0.8.1` (tracks `upstream/0.8.1`)
> **Current implementation:** .NET 8 standalone LiteNetLib relay/listening server. It is not yet an independent, headless ONI simulation.

---

## Direction agreed with ItsLuke

- ItsLuke is prioritizing seamless normal multiplayer; Malionaro is taking the dedicated-server feature forward.
- Work in this order: **Windows → Linux → Docker/ZimaOS → macOS**.
- Prefer separate downloadable server applications from GitHub Releases so mod updates cannot remove the app, configuration, or saves. The earlier launcher-script idea remains useful where appropriate: `.bat` on Windows, `.sh` on Linux, and `.command` on macOS.
- ItsLuke can review the implementation through a PR and offered to build/test the macOS version.
- Keep configuration and save data outside the mod installation directory.
- Docker support should account for ItsLuke's ZimaOS server, including persistent storage and configurable networking.

---

## 1. Current implementation

- [x] Replace the dedicated server's Riptide transport with LiteNetLib 1.3.1.
- [x] Add configurable server startup options and environment overrides.
- [x] Store config and saves in persistent paths outside the mod directory by default.
- [x] Add non-interactive operation, graceful shutdown handling, and a configurable tick rate.
- [ ] Publish and validate a standalone Windows build; the code is present, but release packaging and runtime validation are still pending.

---

## 2. Platform roadmap

### Phase 1 — Windows

- [ ] Publish a self-contained `win-x64` standalone `.exe`.
- [ ] Add a `.bat` launcher with clear defaults and startup errors.
- [ ] Verify config and save paths survive replacing/updating the server app.
- [ ] Write a Windows setup guide covering ports, saves, logs, and troubleshooting.
- [ ] Share progress with ItsLuke and open a PR for review.

### Phase 2 — Linux

- [ ] Publish a self-contained `linux-x64` build.
- [ ] Package a Linux `.AppImage` and provide a `.sh` launcher if useful for the chosen packaging.
- [ ] Document permissions, ports, persistent data paths, and optional service setup.

### Phase 3 — Docker / ZimaOS

- [ ] Add a multi-stage `Dockerfile`.
- [ ] Add a `docker-compose.yml` with persistent config/save volumes and required UDP/TCP port mappings.
- [ ] Expose server settings through environment variables.
- [ ] Add a container health check and document installation on ZimaOS.

### Phase 4 — macOS

- [ ] Prepare a macOS `.app` and `.command` launcher or a suitable standalone release.
- [ ] Coordinate the build and testing with ItsLuke, who offered to handle macOS builds.
- [ ] Complete macOS only after Windows, Linux, and Docker are in a usable state.

---

## 3. Release and collaboration

- [ ] Publish platform artifacts as separate GitHub Release downloads rather than relying on files inside the mod directory.
- [ ] Keep configuration and save data in persistent user-selected or OS-appropriate directories.
- [ ] Open a PR for ItsLuke's implementation feedback and code reviews.
- [ ] Send ItsLuke progress updates as milestones are completed.
- [ ] Keep dedicated-server work isolated from the normal multiplayer work ItsLuke is prioritizing.

---

## Final milestone — Headless, server-authoritative ONI

> Start this only after the current dedicated server is complete, stable, and working for players. This is the long-term follow-up, not a prerequisite for finishing the current server.

- [ ] Test whether ONI can load and simulate a colony headlessly on the server machine.
- [ ] Find a supported way for the server to obtain the ONI runtime and required game files, with the goal that the server operator does not need a normal ONI installation.
- [ ] Move world simulation and save ownership to the dedicated process so no player has to act as master.
- [ ] Make player clients send actions to the server and receive authoritative world state; each client continues to render its own game.
- [ ] Define server behavior when nobody is connected, including pause/resume and save persistence.
- [ ] Remove player-master election and migration from the dedicated-server flow once the server itself is authoritative.
