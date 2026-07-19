# Steam Workshop Release Requirements

Research date: July 19, 2026. ONI Together is published as one Steam Workshop item whose content directory includes the runtime assets for every supported platform. It is not split into macOS and Windows packages.

## Content directory

Valve's `SetItemContent` accepts a local content directory and recommends leaving its files uncompressed. Point the uploader at the Mod root shown below, not a ZIP file or its parent directory.

```text
ONI_Together/                         # Workshop content directory
├── ONI_Together.dll                  # Mod assembly
├── mod.yaml                          # title, description, and staticID
├── mod_info.yaml                     # ONI loader and compatibility metadata
├── assets/
│   ├── windows/oni_mp_ui_assets
│   ├── mac/oni_mp_ui_assets
│   └── linux/oni_mp_ui_assets
├── translations/
│   └── de.mo
└── archived_versions/                # optional; each child needs mod_info.yaml
```

The Mod selects `assets/<platform>` at runtime. Steam receives the entire directory as one Workshop item.

## ONI metadata and preview image

- `mod_info.yaml` must be UTF-8 without a BOM and must be stored at the Mod root. Klei's current format requires `minimumSupportedBuild`; a DLL Mod must declare `APIVersion: 2`. The `version` field is display-only. Every `archived_versions/<name>/` directory needs its own `mod_info.yaml`.
- The current U55 format uses `requiredDlcIds` and `forbiddenDlcIds` lists. `supportedContent` is deprecated, although an archived branch may retain the older format. Omit the lists when the build has no DLC restriction.
- `mod.yaml` retains the project's title, description, and `staticID`. Keep the same `staticID` when updating an item so ONI does not treat the Workshop and local builds as different Mods.
- A Workshop update sets the title, description, visibility, tags, content directory, change note, and main preview image. Call `SetItemPreview` before `SubmitItemUpdate`. JPG, PNG, and GIF work in both the Steam client and website. Each extra file passed to `AddItemPreviewFile` must be smaller than 1 MB.
- The preview image is an upload field, not a replacement for the Mod content directory. Steam Cloud must have nonzero byte and file quotas before preview upload can succeed.

## Upload tools

### Oxygen Not Included Uploader

Install **Oxygen Not Included Uploader** from **Steam Library → Tools**. Klei's uploader creates or updates the Mod, collects the Workshop metadata, and submits the content directory. Exit ONI before uploading so the game does not retain a lock on the DLL.

### Steamworks ISteamUGC

A custom uploader follows this sequence:

1. Call `ISteamUGC::CreateItem` for a new item.
2. Save the returned `PublishedFileId_t`.
3. Call `StartItemUpdate`, then set the title, description, visibility, tags, content directory, preview image, and change note.
4. Call `SubmitItemUpdate` and inspect `SubmitItemUpdateResult_t.m_eResult`.

Steamworks App Admin must enable ISteamUGC file transfer and configure Steam Cloud quotas. `steamcmd.exe +workshop_build_item` can upload a VDF-defined item, but Valve recommends it only for testing because it requests Steam credentials. Use the ONI Uploader or an integrated ISteamUGC client for the public release.

## AppID and Workshop item ID

- Oxygen Not Included uses Steam AppID **457140**.
- The current ONI Together item uses `PublishedFileId` **3630759126**: <https://steamcommunity.com/sharedfiles/filedetails/?id=3630759126>.
- `CreateItem` must use consumer AppID `457140`, not the uploader's AppID. Save the new `PublishedFileId_t` when creating a replacement item and reuse it for later updates.

An ISteamUGC update to the existing item uses:

```text
StartItemUpdate(457140, 3630759126)
SetItemContent(<content directory>)
SetItemPreview(<preview image path>)
SetItemTitle / SetItemDescription / SetItemTags / SetItemVisibility
SubmitItemUpdate(<change note>)
```

A SteamCMD VDF update must also set both `appid=457140` and `publishedfileid=3630759126`. A missing or zero `publishedfileid` creates a new item. Set only the metadata fields intended to change; omitted fields retain their existing values. An upload cannot be cancelled after submission.

## Pre-release checks

- Upload one directory. Do not upload the old platform-specific ZIP archives.
- Accept the Steam Workshop Legal Agreement for the publishing account and set the intended visibility after submission.
- Keep `mod_info.yaml` at the Mod root and declare `APIVersion: 2`. Klei rejects Mods that include public game data or use obfuscation that prevents review.
- Configure Steam Cloud preview quotas and enable ISteamUGC. Incorrect AppIDs or disabled transfer support can return `InvalidParam`.
- Update `minimumSupportedBuild`, DLC restrictions, or `archived_versions` from a build that was tested against the current ONI Live branch.
- Package from a clean Release build, verify the generated content inventory, and confirm that the Workshop description points to this personal development fork and credits the unmaintained upstream repository.

## Primary sources

- [Steam Workshop Implementation Guide](https://partner.steamgames.com/doc/features/workshop/implementation)
- [ISteamUGC Interface](https://partner.steamgames.com/doc/api/isteamugc)
- [Steam Workshop Overview](https://partner.steamgames.com/doc/features/workshop?language=english)
- [Oxygen Not Included Steam store page](https://store.steampowered.com/app/457140/)
- [Klei: Setting up mod_info.yaml and archived_versions](https://forums.kleientertainment.com/forums/topic/158363-setting-up-mod_infoyaml-and-archived_versions/)
- [Klei: How to create a basic Mod for ONI](https://forums.kleientertainment.com/forums/topic/107833-tutorial-how-to-create-a-basic-mod-for-oni/)
- [Klei: Modding System Now In Testing](https://forums.kleientertainment.com/forums/topic/104533-modding-system-now-in-testing/)
- [Klei: Oxygen Not Included update 658361](https://forums.kleientertainment.com/game-updates/oni-alpha/658361-r2487/)
