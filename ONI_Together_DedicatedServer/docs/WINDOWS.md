# Windows

## Run self-contained `.exe`
Download/`publish-windows.ps1` → `ONI_Together_DedicatedServer.exe`. Use `ONI_Together_DedicatedServer.bat` launcher next to it. Data defaults `%AppData%\ONI_Together_DedicatedServer`; launcher uses `.\data` next to app. Override `ONI_DATA_DIR`, `ONI_SERVER_CONFIG_DIR`, `ONI_SERVER_SAVE_DIR`. Open UDP 7777 in Windows Firewall. `Ctrl+C` (SIGINT) / console `quit` graceful. Logs to console.

## Publish
```powershell
./ONI_Together_DedicatedServer/scripts/publish-windows.ps1 -Configuration Release -Output .\artifacts\win-x64
```
Self-contained `win-x64`, `PublishSingleFile`. Artifacts outside mod folder. Config/saves survive replacing app. No Mac/Linux build on Windows.
