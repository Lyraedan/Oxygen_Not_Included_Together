# 安装与联机

## Steam Workshop 安装

正式版只通过 [Steam Workshop 条目 3630759126](https://steamcommunity.com/sharedfiles/filedetails/?id=3630759126) 发布，不再提供 macOS、Windows 或 Linux 独立压缩包。

1. 在 Workshop 页面点击 **Subscribe**。
2. 等待 Steam 下载完成，再启动 Oxygen Not Included。
3. 打开 **Mods**，为当前 DLC 启用 `Oxygen Not Included Together`。
4. 接受游戏重启。
5. 重启后从主菜单进入 **Multiplayer**。

所有参与者必须满足以下条件：

- 游戏版本为 `U59-740622-S`；
- 使用相同 DLC 组合；
- 启用相同 Mod，并保持相同加载顺序与配置；
- 使用 Steam 下发的同一个 ONI Together 版本。

握手会校验游戏 build、协议版本、packet registry fingerprint、Mod 版本、主 DLL SHA-256、DLC 集合与启用 Mod fingerprint。任一项不同都会拒绝连接。

## Steam 与 LAN 联机

Steam 联机由宿主创建 lobby，客户端从 Multiplayer 界面加入。

LAN 默认使用以下端口：

- UDP `8080`：实时通信；
- TCP `8081`：大存档传输。

可配置的 UDP 端口范围为 `1..65534`。修改 UDP 端口后，防火墙也要放行相邻的 TCP 端口。

## 本地开发版

本地构建只用于开发与调试。不要同时启用具有相同 `staticID` 的 Workshop 版和本地版，也不要让联机双方混用两种来源。

macOS 本地开发目录：

```text
~/Library/Application Support/unity.Klei.Oxygen Not Included/mods/Local/ONI_Together
```

Windows 本地开发目录：

```text
%USERPROFILE%\Documents\Klei\OxygenNotIncluded\mods\Local\ONI_Together
```

## 命令行启动

已经登录 Steam 的 macOS 机器可以直接启动游戏：

```bash
/usr/bin/open 'steam://run/457140'
```

第二台 Mac 已安装游戏时，可以通过 SSH 启动：

```bash
ssh user@host "open -a 'Oxygen Not Included'"
```

## Workshop 发布

先构建 `Release`，再生成唯一内容目录：

```bash
./scripts/package_workshop.sh
```

脚本输出：

```text
dist/ONI_Together-workshop/
dist/ONI_Together-workshop-preview.png
```

内容目录根部包含 `ONI_Together.dll`、`mod.yaml`、`mod_info.yaml`、许可证、翻译和 `assets/windows|mac|linux`。目录里没有平台 zip，也没有 PDB。

在 Steam 的 **Library → Tools** 安装并启动 **Oxygen Not Included Uploader**。发布前退出游戏，避免 DLL 被占用。选择 `dist/ONI_Together-workshop` 作为内容目录，选择 `dist/ONI_Together-workshop-preview.png` 作为主预览图，然后粘贴：

- 描述：`workshop/description.bbcode`
- 更新说明：`workshop/changenote.txt`

更新现有条目时选择 PublishedFileId `3630759126`。不要创建新条目，否则订阅者不会迁移，代码中的更新检查也仍会指向旧条目。

平台要求和依据见 [docs/steam_workshop_release_requirements.md](docs/steam_workshop_release_requirements.md)。

## 发布后检查

- Steam 页面显示版本 `1.0.0` 和新的更新说明；
- 订阅后只产生一个 Workshop Mod；
- 下载内容同时包含三个平台资源目录；
- `mod_info.yaml` 为 UTF-8 无 BOM，包含 `minimumSupportedBuild: 740622`、`version: 1.0.0`、`APIVersion: 2`；
- macOS、Windows 和 Linux 分别能加载自己的 UI asset bundle；
- 两台机器可创建 lobby、加入、加载快照并完成一次断线重连。
