# Installation and Multiplayer Setup

## Install from Steam Workshop

Subscribe to the current ONI Together Workshop item, wait for Steam to finish downloading it, and then start Oxygen Not Included.

1. Open **Mods**.
2. Enable `Oxygen Not Included Together` for the active DLC.
3. Accept the restart.
4. Open **Multiplayer** from the main menu after the game restarts.

Every player must use an `ONI_Together.dll` with the same SHA-256 and enable the same DLC set. The handshake reports the exact DLL or DLC mismatch before world transfer. Game build metadata, protocol numbers, packet registry metadata, Mod version metadata, other enabled Mods, load order, and Mod configuration do not block admission.

## Install from source

Source installation uses the repository's existing MSBuild deployment targets. Docker is not required. The default environment is Windows with PowerShell, Git, the .NET 8 SDK, and a legally installed Steam copy of Oxygen Not Included.

Clone the personal development fork:

```powershell
git clone https://github.com/Ericbai06/Oxygen_Not_Included_Together.git
Set-Location .\Oxygen_Not_Included_Together
```

Create the ignored per-machine build configuration:

```powershell
Copy-Item .\Directory.Build.props.default .\Directory.Build.props.user
notepad .\Directory.Build.props.user
```

Set `GameLibsFolder` to the game's Managed assembly directory and `ModFolder` to the ONI development-mod directory. A standard Steam installation normally uses:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project>
  <PropertyGroup>
    <GameLibsFolder>C:\Program Files (x86)\Steam\steamapps\common\OxygenNotIncluded\OxygenNotIncluded_Data\Managed</GameLibsFolder>
    <ModFolder>$(UserProfile)\Documents\Klei\OxygenNotIncluded\mods\dev</ModFolder>
  </PropertyGroup>
</Project>
```

Adjust the paths when Steam or the Windows Documents folder is stored elsewhere. `Directory.Build.props.user` is ignored by Git and must remain machine-local.

Restore the repository tool and NuGet dependencies, then build Release:

```powershell
dotnet tool restore
dotnet restore .\ONI_Together\ONI_Together.csproj
dotnet build .\ONI_Together\ONI_Together.csproj `
  --no-restore `
  --configuration Release `
  -p:TargetGameVersion=740622
```

MSBuild performs the complete development deployment:

- `Publicise` regenerates the publicized game assemblies when the installed game assemblies change.
- `GenerateModYaml` and `GenerateModInfoYaml` create `mod.yaml` and `mod_info.yaml`.
- `ILRepack` merges the mod and its private dependencies into `ONI_Together.dll`.
- `CopyModsToDevFolder` copies the DLL, PDB, YAML metadata, translations, and all platform asset bundles to `$(ModFolder)\ONI_Together_dev`.

With the sample configuration, the deployed directory is:

```text
%USERPROFILE%\Documents\Klei\OxygenNotIncluded\mods\dev\ONI_Together_dev
```

Start Oxygen Not Included, enable the development build in **Mods**, and accept the restart. Disable any subscribed Workshop copy first. Copy the same resulting `ONI_Together.dll` to every multiplayer peer, then enable the same DLCs on every machine.

## Steam and LAN play

Steam play uses a friends-only lobby created by the host. Each participant must run the game from a separate Steam account.

1. The host opens **Multiplayer**, selects Steam, and creates a lobby.
2. The host shares the displayed lobby code or sends a Steam invite.
3. The client opens **Multiplayer** and joins with that code or accepts the invite.
4. The client downloads the host snapshot and enters the colony after the Ready acknowledgement.

Steam sessions use SteamNetworkingSockets for NAT traversal and relay selection. They do not require a public IP address, router port forwarding, VPN, Tailscale, or another LAN tunnel. Both Steam clients must be online, and every peer must pass the DLL SHA-256 and active-DLC checks listed above.

Direct LAN is a separate transport. It defaults to:

- UDP `8080` for live traffic;
- TCP `8081` for large save transfers.

The configurable UDP range is `1..65534`. When the UDP port changes, allow the adjacent TCP port through the firewall as well.

## Build the Workshop directory

After a Release build, run:

```bash
./scripts/package_workshop.sh
```

The script creates:

```text
dist/ONI_Together-workshop/
dist/ONI_Together-workshop-preview.png
```

The content directory contains the DLL, generated YAML metadata, licenses, translations, and all three platform asset bundles. It contains no platform-specific ZIP archives or PDB files.

Use `dist/ONI_Together-workshop` as the content directory in **Steam Library → Tools → Oxygen Not Included Uploader**. Use `dist/ONI_Together-workshop-preview.png` as the main preview image. The description and change note are stored in `workshop/description.bbcode` and `workshop/changenote.txt`.

Release requirements are recorded in [docs/steam_workshop_release_requirements.md](docs/steam_workshop_release_requirements.md).
