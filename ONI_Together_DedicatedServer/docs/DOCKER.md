# Docker / ZimaOS

Not built or run yet (no Docker daemon on the development machine).

## Build
```sh
docker build -f ONI_Together_DedicatedServer/Dockerfile -t oni-together-dedicated-server .
```
Multi-stage `mcr.microsoft.com/dotnet/sdk:8.0` → `mcr.microsoft.com/dotnet/runtime:8.0`, non-root `app`, `EXPOSE 7777/udp`, `VOLUME /data`. The build context is the repository root; `Dockerfile.dockerignore` next to the Dockerfile keeps `bin/`, `obj/` and the rest out (needs BuildKit, the default since Docker 23).

Health: the server writes `/data/config/heartbeat` every 5 seconds; `--health-check` reports unhealthy when it is missing or older than 30 seconds.

## Compose
```sh
docker compose -f ONI_Together_DedicatedServer/docker-compose.yml up -d
```
Persistent named volume `oni-data` (or bind-mount `./data`). `restart`, `stop_grace_period`. Maps `7777/udp`. Sets `ONI_NON_INTERACTIVE=true`, `ONI_DATA_DIR=/data`, `ONI_SERVER_CONFIG_DIR`, `ONI_SERVER_SAVE_DIR`. Env passthrough for all `ONI_*`. Ensure `/data` writable by container user.

## ZimaOS
Add as custom app/container; map UDP 7777; persistent volume for `/data`; set env. Put the first colony into `/data/saves` and set `ONI_SAVE_FILE` once (see `RELAY.md`). A player's game hosts the colony (current relay). LAN auto-discovery does not cross the Docker bridge: join by IP:port, or use `network_mode: host`.
