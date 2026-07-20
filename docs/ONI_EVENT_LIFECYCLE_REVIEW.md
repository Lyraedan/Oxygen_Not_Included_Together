# ONI Together 事件与生命周期审查（U59 / v1.0.4）

审查结论：当前日志能证明两类崩溃级问题，二者都来自生命周期所有权混乱，不应在 `PrimaryElement`、`Constructable`、`Deconstructable` 等组件里逐个加空值保护。

1. 建筑完成事件同时走了 `BuildingDef.Build` 和通用 `SpawnPrefabPacket` 两条物化路径，客户端短时间内出现两个同 NetId 的 `BuildingComplete`。
2. cleanup 回调使用会创建身份的 `GetNetIdentity()`，可能在 Unity 正在销毁对象时调用 `AddComponent<NetworkIdentity>()`。

当前工作区已经分别加入共享修复。没有发现第三项必须进入 v1.0.4 的、可由日志或确定调用链证明的崩溃级修复。审查发现的事件发布顺序风险也已在同一链路修正，但它不是本次 `OnSpawn` 崩溃的原因。

## 审查范围与证据边界

- 比较基线：`v1.0.2`（`338bb40a294e0b493fd0b203ad2edb687007747c`）到当前工作区。
- 游戏版本：`Build U59-740622-S`。
- 主机日志：`/Users/eric/Library/Logs/Klei/Oxygen Not Included/Player.log`。
- Windows 客户端日志：`C:\Users\ALIENWARE\AppData\LocalLow\Klei\Oxygen Not Included\Player.log`。
- 当前游戏程序集：
  - `Assembly-CSharp.dll` SHA-256：`c518e225114797faa7caebad840016170a6c88343e8d371d558bcf01581d0c1a`
  - `Assembly-CSharp-firstpass.dll` SHA-256：`a5e6ecac282327854f203b7f85e7628219ceb5aa05d9a87d23c2f097d2a97397`

Klei 的公开资料确认 ONI 原生加载 DLL/Harmony mod，但没有公布 `KMonoBehaviour.OnSpawn`、`Constructable.FinishConstruction`、`BuildingDef.Build` 等内部生命周期的完整 API 契约。Klei 也明确说明只能尽量减少 mod 破坏，并只维护有限的 `ModUtil` API。因此，涉及这些内部接口的结论必须绑定具体游戏程序集版本，不能把旧社区教程当作接口保证。

- [Klei：Modding System Now In Testing](https://forums.kleientertainment.com/forums/topic/104533-modding-system-now-in-testing/)
- [Klei：Upcoming Mod Support Changes](https://forums.kleientertainment.com/forums/topic/137918-upcoming-mod-support-changes/)
- [Harmony：Execution Flow](https://harmony.pardeike.net/articles/execution.html)
- [Harmony：Finalizer](https://harmony.pardeike.net/articles/patching-finalizer.html)
- [Unity：Object.Destroy](https://docs.unity3d.com/cn/current/ScriptReference/Object.Destroy.html)

## 领域模型

### 事件类别

- **权威游戏事件**：只由 host 执行的原始 ONI 行为，例如 `Constructable.FinishConstruction`。
- **专用物化事件**：知道 ONI 领域初始化规则的 packet，例如 `BuildCompletePacket`。建筑必须通过 `BuildingDef.Build` 物化。
- **通用生命周期事件**：只描述 prefab、位置、NetId、revision 和存活状态的 `SpawnPrefabPacket` / `DespawnEntityPacket`。
- **绑定事件**：`BindExistingOnly=true` 的生命周期 packet。它只把权威 NetId/revision 绑定到已由专用物化器创建的对象，不能再创建 prefab。
- **领域状态事件**：对象存在后才有意义的开关、逻辑端口、动画、存储等状态。
- **终止事件**：cleanup / despawn / tombstone。进入终止阶段后不得重新注册、广播 spawn 或补加组件。

### 必须保持的约束

1. host 是游戏状态变更的唯一权威来源。
2. 一次领域事件只能有一个对象物化器。
3. 需要 ONI 专用初始化的对象，顺序必须是“专用物化 → bind-existing lifecycle → 领域状态”。
4. `BuildingComplete` 激活前，`PrimaryElement.Element`、施工材料、温度、朝向和 facade 必须由 `BuildingDef.Build` 初始化。
5. cleanup 路径只能查询已有身份，不得调用任何可能 `AddComponent`、注册 NetId 或广播 spawn 的 accessor。
6. `BeginManagedSpawn()` 必须由 Postfix 和 Finalizer 共同保证只结束一次；原方法抛异常时也必须恢复 suppression depth。
7. reliable packet 只能表达已经成功发生的权威结果；不能在原始游戏操作成功前提前发布结果。

## Finding 1：completed building 被物化两次（崩溃级，高置信）

### 可复现证据

客户端同一事件的日志顺序是稳定的：

```text
[12:38:40.262] [BuildCompletePacket] Finalized Outhouse at cell 88397
[12:38:40.270] [NetEntityRegistry] NetId collision -339437323: OuthouseComplete vs OuthouseComplete
Error in OuthouseComplete.PrimaryElement.OnSpawn at (77.50, 120.01, -19.50)
Error in OuthouseComplete.Deconstructable.OnSpawn at (77.50, 120.01, -19.50)
```

Ladder、Tile、Wire 和 TilePOI 具有同一顺序。Ladder 在 cell `88415` 的记录是：完成 packet、两次同 prefab NetId collision，随后连续出现 `PrimaryElement.OnSpawn` 和 `Deconstructable.OnSpawn` 异常。TilePOI 还会继续触发 `SimTemperatureTransfer.OnSpawn` / `SimRegister` 异常。

### 确定调用链

1. host 的 `Constructable.FinishConstruction` 调用 U59 `BuildingDef.Build`，创建并激活合法的 `BuildingComplete`。
2. `BuildingComplete.OnPrefabInit` patch 给所有完成建筑声明 `NetworkIdentity`。
3. `NetworkIdentity.OnSpawn` 注册身份并自动发送通用 `SpawnPrefabPacket`。
4. 同一 `FinishConstruction` patch 还发送专用 `BuildCompletePacket`。
5. 客户端 `BuildCompletePacket.OnDispatched` 删除施工对象，再调用 `BuildingDef.Build`。
6. 客户端通用 `SpawnPrefabPacket` 对非元素 prefab 使用 `Util.KInstantiate(prefab, position)` 并激活。它没有执行 `BuildingDef.Create/Build` 的材料初始化。
7. 两条 packet 路径均可创建完成建筑。Unity 的 `Object.Destroy` 实际销毁推迟到当前 Update 结束，所以短时间内两个对象并存，确定产生同 NetId collision。

U59 反编译进一步确认：

- `PrimaryElement.OnSpawn` 会枚举 `Element.attributeModifiers`；日志偏移 `0x10` 对应 `Element` 未初始化。
- `SimTemperatureTransfer.OnSpawn` 和 `SimRegister` 同样读取 `PrimaryElement.Element`、`Mass` 等字段。
- `BuildingDef.Instantiate` 会给施工对象设置 `PrimaryElement.ElementID` 和 `Constructable.SelectedElementsTags`。
- `BuildingDef.Build/Create` 会按选材创建完成建筑，并填写 `Deconstructable.constructionElements`。
- `Util.KInstantiate` 只复制 prefab 和执行通用 prefab 初始化，不能替代 `BuildingDef.Build`。

因此错误类名会随着 prefab 组件变化，根因仍是同一个：通用物化器激活了缺少领域初始化的完成建筑。

### 最小共享修复评估

当前 `ConstructablePatch` 的修复：

- Prefix 在 host 原始 `FinishConstruction` 前调用 `NetworkIdentity.BeginManagedSpawn()`，屏蔽 `BuildingDef.Build` 期间的自动通用 spawn。
- 成功路径先 `EndManagedSpawn()`，确认 host 的实际完成建筑并生成 `BindExistingOnly=true` 的生命周期 packet，再按“`BuildCompletePacket` → lifecycle”顺序发送。
- Finalizer 在原始方法或 Postfix 抛异常时恢复 suppression depth，并返回原异常，不吞游戏异常。

这一次改动覆盖所有经 `Constructable.FinishConstruction` 完成的建筑类型，不需要为 Ladder、Outhouse、Tile、Wire、POI 分别修补。Harmony 官方执行模型也支持该结构：原方法抛异常时 Postfix 不执行，必须用 Finalizer 做资源清理。

### 验收要求

静态 IL 测试只能证明 patch 中存在 Begin/End/Finalizer 和发送顺序，不能证明 Unity 运行时没有第二个对象。发布前至少要完成一次双机定向验证：

- 连续完成 Outhouse、Ladder、Tile、Wire 和一个带 `SimTemperatureTransfer` 的建筑。
- client 日志中以下字符串计数保持为 0：
  - `NetId collision.*Complete vs .*Complete`
  - `Error in .*Complete.PrimaryElement.OnSpawn`
  - `Error in .*Complete.Deconstructable.OnSpawn`
  - `Error in .*Complete.SimTemperatureTransfer.OnSpawn`
  - `Error in .*Constructable.OnSpawn`
- 每次完成后 host/client 同 cell 只有一个 `BuildingComplete`，且 NetId 一致。
- 中途重连一次，确认 lifecycle baseline 使用 bind-existing，不创建第二栋建筑。

## Finding 2：cleanup 阶段创建 NetworkIdentity（崩溃级，高置信）

### 可复现证据

host 日志反复出现：

```text
Can't add component to object that is being destroyed.
[LogicBuildingCleanup] System.NullReferenceException
at ONI_Together.Networking.Extensions.GetNetIdentity(GameObject)
at ONI_Together.Networking.Components.LogicStateSyncer.Unregister(GameObject)
at LogicBuildingCleanupRegistrationPatch.Postfix(Building)
```

### 确定调用链

1. Harmony cleanup patch 是 `Building.OnCleanUp` 的 Postfix，此时原 cleanup 已经执行。
2. 旧 `LogicStateSyncer.Unregister` 调用 `go.GetNetIdentity()`。
3. `Extensions.GetNetIdentity(GameObject)` 在找不到身份时会执行 `go.AddComponent<NetworkIdentity>()`，随后注册并广播 spawn。
4. Unity 正在销毁该 GameObject 时拒绝 `AddComponent`，随后代码继续解引用创建结果并出现 NRE。

`Pickupable.OnCleanUp` Postfix 原来也使用同一个会创建身份的 accessor。当前日志没有证明它已经触发崩溃，但它违反同一条确定的 cleanup 约束，属于同类潜在错误。

### 最小共享修复评估

当前工作区已把两个 cleanup accessor 改成只读查询：

- `LogicStateSyncer.Unregister` 使用 `go.GetComponent<NetworkIdentity>()`。
- `PickupableCleanedUpPatch.Postfix` 使用 `__instance.GetComponent<NetworkIdentity>()`。

该修改不会创建组件、注册 NetId 或发送 lifecycle packet。身份不存在时直接结束 cleanup，符合终止阶段语义。其余已审查的 cleanup patch 中，RemoteWorkerDock、SelfChargingElectrobank、HighEnergyParticle 等在原 cleanup 前用 Prefix 捕获 NetId，未复现相同的 Postfix-after-destruction 路径。

### 验收要求

- 单元测试应检查 cleanup 调用图不再引用 `Extensions.GetNetIdentity(GameObject)`。
- 双机反复建造/拆除逻辑建筑并拾取、销毁地面物品。
- host/client 日志中以下字符串计数保持为 0：
  - `Can't add component to object that is being destroyed`
  - `[LogicBuildingCleanup]`
  - `[PickupableCleanedUpPatch] Exception`
- 销毁后 registry 中无残留 NetId，客户端也不出现已销毁对象的二次 spawn。

## Finding 3：BuildComplete 结果曾在 host 原操作成功前发送（已修复的确定风险）

旧 Prefix 先构造并发送 `BuildCompletePacket`，随后才进入 U59 原始 `Constructable.FinishConstruction`。如果原方法抛异常或被其他 Harmony patch 跳过，client 已经收到并执行完成建筑，而 host 没有完成建筑。Finalizer 只能恢复 suppression depth，不能撤回已经发出的可靠 packet。

这是确定的事件顺序缺陷，但当前日志没有显示它触发过，也不是 `PrimaryElement.OnSpawn` 的成因。v1.0.4 已把发送移到成功 Postfix，并保持顺序：

```text
host FinishConstruction 成功
→ EndManagedSpawn
→ 查找 host 实际完成建筑
→ 发送 BuildCompletePacket
→ 发送 BindExistingOnly lifecycle
```

Prefix 只负责捕获参数和开启 managed-spawn scope。这样 packet 表达的是已成功发生的结果，并保持专用物化先于 lifecycle 绑定。排序测试同时禁止 Prefix 发送结果，并验证 Postfix 在结束 suppression 后才发送可靠 packet。

## 其余潜在面审查

本轮逐项检查了：

- 所有 `BeginManagedSpawn` / `EndManagedSpawn` 调用；现有 Minnow、CryoTank、EntityDeliver 路径均有对应 finally/finalizer 或结构测试。
- `SpawnPrefabPacket` 的元素物化、普通 prefab 物化、bind-existing policy、revision baseline 和 tombstone。
- 所有 `OnCleanUp` / `OnForcedCleanUp` patch 及其中的 `GetNetIdentity()` 使用。
- `BuildCompletePacket`、`BuildStatePacket`、`UtilityBuildStatePacket` 的完成建筑处理。
- DLC runtime prefab identity patch 和专用 spawn packet。

没有足够证据把以下项目升级为 v1.0.4 崩溃修复：

- `BuildCompletePacket` 在目标 cell 找不到施工对象时只记录 Finalized 而不创建建筑：属于丢包/乱序后的状态一致性风险，当前 reliable ordered 流程和日志未证明触发。
- `LogicStateSyncer` 客户端对已销毁 tracked entry 的延迟回收：可能造成状态表残留，当前没有崩溃或无界增长证据。
- 通用 `SpawnPrefabPacket` 对普通非元素 prefab 使用 `KInstantiate`：对于声明 `SaveLoadRoot` 的 baseline 对象，现有 binding policy 会要求 bind-existing；未发现另一类已被专用 packet 物化、又会被通用 packet 重复创建的确定运行时路径。
- `InstantiationsPacket` 的批量 `Object.Instantiate`：当前拦截入队代码未启用，属于 dormant 路径。

## 发布门禁

v1.0.4 可以在以下条件同时满足后发布：

1. completed-building suppression、bind-existing lifecycle、cleanup 只读身份查询均已编译进最终 DLL。
2. 相关单元测试实际运行通过；不能只报告编译通过。
3. 完成上述 Outhouse/Ladder/Tile/Wire/POI 双机矩阵，日志关键字为 0。
4. 断线重连后，完成建筑数量、prefab、cell、NetId 和 lifecycle revision 一致。
5. 发布包 DLL SHA-256 与本地和 Steam Workshop 上传内容一致。

如果只完成静态测试，结论应表述为“已修复确定调用链，等待双机运行验收”，不能表述为“已证明不会崩溃”。
