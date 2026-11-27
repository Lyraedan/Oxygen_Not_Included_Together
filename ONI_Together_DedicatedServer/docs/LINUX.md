# Linux

## Run self-contained `linux-x64`
`sh publish-linux.sh` or download release. Use `ONI_Together_DedicatedServer.sh`. `chmod +x`. Data `~/.config/ONI_Together_DedicatedServer` by default; override `ONI_DATA_DIR` etc. Open UDP 7777. SIGTERM graceful. Run as non-root; ensure data dir writable.

## AppImage
`make-appimage.sh <publish-dir> [out]` (needs `appimagetool` on PATH; not testable on Windows). Or use self-contained binary + `.sh`.

## systemd (optional)
```ini
[Unit]
Description=ONI Together Dedicated Server
After=network.target
[Service]
Type=simple
User=oni
WorkingDirectory=/opt/oni-server
ExecStart=/opt/oni-server/ONI_Together_DedicatedServer.sh --non-interactive
Restart=unless-stopped
Environment=ONI_DATA_DIR=/var/lib/oni-server
[Install]
WantedBy=multi-user.target
```
