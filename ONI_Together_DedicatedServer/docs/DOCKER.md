# Docker / ZimaOS

No Docker daemon in this environment; files provided, not built/tested here.

## Build
```sh
docker build -f ONI_Together_DedicatedServer/Dockerfile -t oni-together-dedicated-server .
```
Multi-stage `mcr.microsoft.com/dotnet/sdk:8.0` → `mcr.microsoft.com/dotnet/runtime:8.0`, non-root `app`, `EXPOSE 7777/udp`, `HEALTHCHECK --health-check`, `VOLUME /data`.

## Compose
```sh
docker compose -f ONI_Together_DedicatedServer/docker-compose.yml up -d
```
Persistent named volume `oni-data` (or bind-mount `./data`). `restart`, `stop_grace_period`. Maps `7777/udp`. Sets `ONI_NON_INTERACTIVE=true`, `ONI_DATA_DIR=/data`, `ONI_SERVER_CONFIG_DIR`, `ONI_SERVER_SAVE_DIR`. Env passthrough for all `ONI_*`. Ensure `/data` writable by container user.

## ZimaOS
Add as custom app/container; map UDP 7777; persistent volume for `/data`; set env. Health check uses `--health-check`. Requires master client with ONI + mod (current relay). 
