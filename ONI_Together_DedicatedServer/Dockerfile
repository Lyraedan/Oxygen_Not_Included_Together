# Multi-stage Dockerfile for ONI Together Dedicated Server (.NET 8).
# Build from the REPOSITORY ROOT:
#   docker build -f ONI_Together_DedicatedServer/Dockerfile -t oni-dedicated-server .
# See docs/DOCKER.md.

# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy build props + project. (Repo-root context.)
COPY Directory.Build.props Directory.Build.targets Directory.Build.props.default ./
COPY ONI_Together_DedicatedServer/ ./ONI_Together_DedicatedServer/

RUN dotnet restore ./ONI_Together_DedicatedServer/ONI_Together_DedicatedServer.csproj
RUN dotnet publish ./ONI_Together_DedicatedServer/ONI_Together_DedicatedServer.csproj \
    -c Release \
    -o /app/publish \
    -p:PublishTrimmed=false \
    -p:PublishSingleFile=false

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app

COPY --from=build --chown=app:app /app/publish ./

# Persistent data outside the image; mount a volume at /data.
RUN mkdir -p /data && chown app:app /data || true
USER app

ENV ONI_DATA_DIR=/data \
    ONI_SERVER_CONFIG_DIR=/data/config \
    ONI_SERVER_SAVE_DIR=/data/saves \
    ONI_NON_INTERACTIVE=true \
    ONI_PORT=7777 \
    ONI_IP=0.0.0.0 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

VOLUME ["/data"]

# LiteNetLib uses UDP. (No TCP port is required for the current relay protocol.)
EXPOSE 7777/udp

# Container health check: app-provided `--health-check` exits 0.
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD dotnet /app/ONI_Together_DedicatedServer.dll --health-check || exit 1

ENTRYPOINT ["dotnet", "/app/ONI_Together_DedicatedServer.dll"]
