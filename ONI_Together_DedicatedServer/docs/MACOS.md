# macOS

macOS builds require a Mac; not buildable/testable on Windows/Linux. ItsLuke offered to build/test mac. Complete mac after Windows/Linux/Docker.

## Build on Mac
```bash
bash ./scripts/publish-macos.command osx-arm64 Release   # or osx-x64
```
## App
```bash
bash ./packaging/macos/make-app.sh <publish-dir> <out>   # creates ONI_Together_DedicatedServer.app
```
`.command` launcher. Data `~/Library/Application Support/ONI_Together_DedicatedServer`. UDP 7777. Codesigning/notarization not handled.
