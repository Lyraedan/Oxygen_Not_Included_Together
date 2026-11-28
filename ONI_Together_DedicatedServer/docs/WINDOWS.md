# Windows

## Run self-contained `.exe`
Download/`publish-windows.ps1` → `ONI_Together_DedicatedServer.exe`. Use the `ONI_Together_DedicatedServer.bat` launcher next to it. Config and saves live in `%AppData%\ONI_Together_DedicatedServer`, never next to the app, because the app may be shipped inside the mod folder that mod updates replace. Override with `ONI_DATA_DIR`, `ONI_SERVER_CONFIG_DIR`, `ONI_SERVER_SAVE_DIR` or `--config-dir`/`--save-dir`. Open UDP 7777 in Windows Firewall. `Ctrl+C` or console `quit` stops it cleanly. First start: see `RELAY.md`.

## Publish
```powershell
./ONI_Together_DedicatedServer/scripts/publish-windows.ps1 -Configuration Release -Output .\artifacts\win-x64
```
Self-contained `win-x64`, `PublishSingleFile`. Artifacts outside mod folder. Config/saves survive replacing app. No Mac/Linux build on Windows.
