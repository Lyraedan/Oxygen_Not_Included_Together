# Steam Workshop 发布要求（Oxygen Not Included / ONI_Together）

查阅日期：2026-07-19。目标是发布一个 Steam Workshop 条目；上传对象是一个内容目录，不再把 macOS、Windows 压缩包拆成两个发布物。

## 1. 内容目录

Valve 的 `SetItemContent` 接收本地内容目录，并明确建议不要把文件合并或压缩成单个 zip。`SetItemContent` 指向下列目录的根，而不是 zip 文件或多包一层的父目录：

```text
ONI_Together/                         # Workshop content folder
├── ONI_Together.dll                  # DLL mod
├── mod.yaml                          # title / description / staticID
├── mod_info.yaml                     # ONI 加载与兼容性元数据
├── assets/
│   ├── windows/oni_mp_ui_assets
│   ├── mac/oni_mp_ui_assets
│   └── linux/oni_mp_ui_assets
├── translations/
│   └── de.mo
└── archived_versions/                # 可选；每个子目录都要有自己的 mod_info.yaml
```

这里的 `assets/<platform>` 是同一 Workshop 内容目录内的运行时资源。平台选择由 Mod 自己处理；Steam 侧只接收这一份目录。

## 2. ONI 元数据与预览图

- `mod_info.yaml` 必须位于 Mod 根目录，并使用 UTF-8（无签名/BOM）。当前 Klei 指南要求 `minimumSupportedBuild`；包含 DLL 时必须写 `APIVersion: 2`。`version` 只用于界面显示。每个 `archived_versions/<name>/` 也要放自己的 `mod_info.yaml`。
- 当前 U55 格式使用 `requiredDlcIds`、`forbiddenDlcIds` 列表；`supportedContent` 已弃用，旧分支归档仍可使用旧格式。没有 DLC 限制时省略这些字段。
- `mod.yaml` 保留本项目已有的 `title`、`description`、`staticID`。更新既有条目时保持 `staticID` 不变，避免 Steam 版本与本地版本被 ONI 视为不同 Mod。
- Workshop 更新要设置标题、描述、可见性、标签、内容目录、变更说明和主预览图。`SetItemPreview` 在 `SubmitItemUpdate` 前调用；主预览应使用网页和应用都能渲染的 JPG、PNG 或 GIF。额外预览文件通过 `AddItemPreviewFile` 添加，单个文件须小于 1 MB。
- 预览图是 Workshop 条目的上传字段，不是替代 Mod 内容目录的 zip 内文件。Steam Cloud 必须配置用户字节额度和文件数，否则预览上传会失败。

## 3. 上传工具与 API

### ONI Uploader（推荐）

在 Steam 的 **Library → Tools** 安装 **Oxygen Not Included Uploader**。Klei 官方教程要求添加 Mod、填写发布信息并 Publish；上传前退出 ONI，避免 DLL 仍被游戏占用导致失败。

### Steamworks ISteamUGC

若使用自有上传器，流程是：

1. `ISteamUGC::CreateItem` 创建条目。
2. 读取并保存返回的 `PublishedFileId_t`。
3. `StartItemUpdate` 后设置标题、描述、可见性、标签、内容目录、预览图和变更说明。
4. `SubmitItemUpdate`，检查 `SubmitItemUpdateResult_t.m_eResult`。

Steamworks App Admin 需要启用 ISteamUGC 文件传输，并配置 Steam Cloud 额度。`steamcmd.exe +workshop_build_item` 可读取 VDF 上传/更新，但 Valve 仅建议把它用于测试，因为它要求输入 Steam 凭据；正式发布优先使用 ONI Uploader 或集成 ISteamUGC。

## 4. AppID 与 Workshop 条目标识

- Oxygen Not Included 的 Steam AppID 是 **457140**（官方商店页 URL 中的 `/app/457140/`）。
- 当前 ONI_Together 条目使用 `PublishedFileId` **3630759126**：<https://steamcommunity.com/sharedfiles/filedetails/?id=3630759126>。
- `CreateItem` 的 consumer AppID 必须是游戏 **457140**，不能填上传工具自己的 AppID。新条目由 Steam 返回新的 `PublishedFileId_t`；把这个数字保存为项目发布配置，后续更新沿用它。

## 5. 更新既有条目

使用 ISteamUGC 时，以现有条目为目标：

```text
StartItemUpdate(457140, 3630759126)
SetItemContent(<同一个内容目录>)
SetItemPreview(<预览图路径>)
SetItemTitle / SetItemDescription / SetItemTags / SetItemVisibility
SubmitItemUpdate(<changenote>)
```

SteamCMD VDF 更新也必须同时填写 `appid=457140` 与 `publishedfileid=3630759126`；把 `publishedfileid` 留空或设为 `0` 会走创建流程。只填写本次要改的键，其他元数据会保持原值。提交后不能取消上传。

## 6. 已知限制与发布前检查

- 上传的是一个目录，不能把当前 macOS/Windows zip 直接作为 Workshop 内容；同一目录内保留所有平台资源子目录。
- 条目在作者接受 Steam Workshop Legal Agreement 前默认隐藏；提交后应打开条目页面完成协议和可见性设置。
- ONI 新 Mod 缺少根目录 `mod_info.yaml` 时无法上传；DLL Mod 缺少 `APIVersion: 2` 会被 ONI 禁用。Klei 也会拒绝包含公共游戏数据或难以审计的混淆代码的 Mod。
- 预览图上传依赖 Steam Cloud 配额；ISteamUGC 未启用或 AppID 不匹配会导致 `InvalidParam` 等结果。
- 不同游戏版本 build 或 DLC 需要通过 `minimumSupportedBuild`、`requiredDlcIds`、`forbiddenDlcIds` 或 `archived_versions` 明确声明；发布前用当前 Live 分支实际测试的 build 更新元数据。

## 来源（仅 Valve/Steamworks 与 Klei 官方）

- [Steam Workshop Implementation Guide](https://partner.steamgames.com/doc/features/workshop/implementation)
- [ISteamUGC Interface](https://partner.steamgames.com/doc/api/isteamugc)
- [Steam Workshop Overview](https://partner.steamgames.com/doc/features/workshop?language=english)
- [Oxygen Not Included（Steam 商店，AppID 457140）](https://store.steampowered.com/app/457140/Oxygen_Not_Included/)
- [Klei：Setting up mod_info.yaml and archived_versions](https://forums.kleientertainment.com/forums/topic/158363-setting-up-mod_infoyaml-and-archived_versions/)
- [Klei：How to create a basic mod for ONI（含 ONI Uploader）](https://forums.kleientertainment.com/forums/topic/107833-tutorial-how-to-create-a-basic-mod-for-oni/)
- [Klei：Modding System Now In Testing](https://forums.kleientertainment.com/forums/topic/104533-modding-system-now-in-testing/)
- [Klei：Oxygen Not Included 更新说明 658361](https://forums.kleientertainment.com/game-updates/oni-alpha/658361-r2487/)
