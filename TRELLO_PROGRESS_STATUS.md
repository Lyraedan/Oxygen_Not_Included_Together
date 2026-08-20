# 📊 Trello Progress Status: Oxygen Not Included Together

> **Quelle:** [Trello Board - Oxygen Not Included Together](https://trello.com/b/kq7yVWyU/oxygen-not-included-together)
> **Stand:** Letzte Aktivität: 2026-08-20

---
## 📈 Übersicht & Zusammenfassung

- **Gesamtzahl Karten (Tasks/Features/Bugs):** 165
- **Requested Features:** 3 Einträge
- **Mod Compatibility Issues:** 3 Einträge
- **To Do Syncronization:** 5 Einträge
- **To Do:** 10 Einträge
- **Known issues:** 15 Einträge
- **Work In progress:** 1 Einträge
- **New features for next update:** 14 Einträge
- **Bugfixes for next update:** 10 Einträge
- **Synced (Live and next update):** 60 Einträge
- **Synched with Issues (Live and next update):** 10 Einträge (3 behoben)
- **Complete:** 32 Einträge
- **Benched (For now):** 2 Einträge

---

## 📋 List: Requested Features (3)

- [ ] **Control duplicants independantly?** `Unlikely`

- [ ] **Lets friends spawn on separate planetoids** `Under Consideration`

- [ ] **Server Option to define how frequently a hard sync happens instead of each cycle. For example hard sync every 3 cycles.** `Under Consideration`

---

## 📋 List: Mod Compatibility Issues (3)

- [ ] **Blueprints Expanded: There is a crash once a building is finished**

- [ ] **Bigger Zoomout: Causes instability since the mod uses the viewport to sync various things**

- [ ] **Bigger Capacity: Can cause instability when syncing storage containers**

---

## 📋 List: To Do Syncronization (5)

- [x] **Status Item Groups Sync** `Code fix` `Runtime QA pending`
  > Status groups now use one reliable OxySync snapshot with support for up to 64 entries. The syncer is attached to every networked, prefab-backed selectable entity and resolves duplicant, creature, robot, building, plant, and miscellaneous status items on clients. Helper objects without a `KPrefabID`, such as `WorldSelectionCollider`, are ignored safely.

- [x] **plant animation and stats sync** `Code fix` `Runtime QA pending`
  > Plants now participate in animation synchronization. Growth age, maturity, wilting, harvest readiness, automatic harvesting, and marked-for-harvest state are replicated from the host. Planted crops explicitly register their animation syncer after spawning, while animated buildings use viewport-aware state snapshots. Synchronized buildings, including the Microbe Musher, additionally replicate their exact ordered Play/Queue events so working intro, loop, and idle transitions cannot be lost when snapshots are coalesced. Client-side state machines can no longer overwrite host-authoritative animations on synchronized entities.

- [x] **gas/liquid in pipe animation sync** `Code fix` `Runtime QA pending`
  > Gas and liquid pipe updates now include the visual flow direction, moving element, and moving mass. Clients rebuild the conduit flow information used by the pipe visualizer in addition to applying the authoritative contents.

- [x] **electricity and food storage correct amount** `Code fix` `Runtime QA pending`
  > Host-authoritative battery charge and storage snapshots were verified. OxySync now resolves incoming fields against every network behaviour on an entity instead of silently dropping fields that are not owned by the first component, restoring client battery meters. Coal and other fueled generators now publish their authoritative fuel mass through OxySync, and client simulation no longer overwrites the received meter with empty local storage. Refrigerators remain included in storage synchronization, and every storage syncer sends its existing contents immediately after spawning instead of waiting for the first inventory change.

- [x] **improve sync experience in 3x speed, by advancing sync logic to reduce screen freeze and lag.** `Code fix` `Runtime QA pending`
  > OxySync processing now uses a fair rotating cursor, a per-tick component cap, and a smaller frame-time budget at triple speed. Real-time transforms for duplicants, critters, and other moving entities are processed first at every network tick and use a no-delay path that cannot get stuck behind the optional packet queue. Client interpolation measures actual snapshot arrival jitter and adapts its buffer between 150 and 350 ms, while lower-priority state serialization remains spread across frames. The experimental duplicant navigation-transition replay was removed after runtime testing showed that it competed with OxySync transform snapshots and repeatedly pulled duplicants back to older path positions. OxySync is again the single authority for remote duplicant positioning.

---

## 📋 List: To Do (10)

- [ ] **Cleanup the codebase** `Issue`
  > The codebase is a mess, it has alot of contributor left over code and genuinely needs to be cleaned up

- [x] **LiteNetLib Network Transport, Standardized Transport Layer (ITransport), LAN Discovery & Dedicated Server** `Code fix` `Next Update`
  > Replaced Riptide's 1000-byte limitation on LAN with LiteNetLib 1.3.1. Implemented a driver-based `ITransport` and `TransportRegistry` layer providing unified abstractions for Steamworks P2P, LiteNetLib (UDP LAN / Direct IP), and Epic Online Services (EOS Crossplay driver). Added zero-configuration UDP broadcast LAN Lobby Auto-Discovery and modernized `ONI_Together_DedicatedServer` to LiteNetLib with headless packet relay and master-migration. High-level gameplay systems (OxySync, Chores, World State) now send payloads transparently without low-level byte-limit concerns or transport coupling.

- [x] **Dragtool: Visual Display Sync** `Code fix` `Runtime QA pending`
  > Local and remote players now see box/line drag areas, their width × height text, and brush footprints. This includes Brush, Sprinkle, Heat, Stress, Destroy, Reveal, Clear Floor, Critter Removal, Priority, Mop, Harvest, Disinfect, Empty Pipe, and Disconnect. The local multiplayer visualizer updates every frame; drag start and final extent are sent immediately to peers so short operations remain visible. Remote drag endpoints follow the interpolated cursor, and the Move To cursor mapping now uses the correct game tool class.

- [ ] **Profile low performance when hosting**

- [ ] **[Testing] Check if critters spawned on the host also spawn on the client, stop clients from spawning their own critters**

- [ ] **Hard Sync at the start of each cycle (As a server option)**

- [ ] **Clients request a new sync state**
  > “can the “not the hosts” have a button where they can request a new sync state that isn’t a hard sync by the host, because for example 3-4 players playing yet only one seems to be having sync issues and the others are fine. Somewhat of a “self-managed” state that doesn’t rely on having to get the hosts attention and do a sync.”

- [ ] **Blueprint synchronization**

- [ ] **Relocate Tool (Items)**

- [ ] **Clients request game speed state on sim load**

---

## 📋 List: Known issues (15)

> **Local development status (2026-08-01):** A code fix is implemented for all 15 entries. The complete solution builds with 0 errors, 6/6 known-issue regression tests pass against the installed ONI assemblies, and all 11 reorganized Harmony target methods were found with matching signatures in game build 700386. The checkboxes document implementation status, not completed multiplayer runtime verification. Real host/client QA in ONI is still required for all 15 entries.

<details>
<summary>Implementation evidence for the 15 code fixes</summary>

| # | Known Issue | Implementation path |
|---:|---|---|
| 1 | Rovers | `Patches/Critters/RoverPatches.cs`, `Scripts/Creatures/CreatureMultiplayerInitializer.cs` |
| 2 | WorldDamage-Duplikate | `Patches/World/WorldDamagePatch.cs`, `Networking/Packets/World/WorldDamageSpawnResourcePacket.cs` |
| 3 | Storage-Deliver-FX | `Patches/World/StoragePatches.cs`, `Networking/Packets/World/StorageItemPacket.cs` |
| 4 | Loading a save during a live session | `Patches/LoadScreenPatch.cs`, `Patches/World/SaveLoaderPatch.cs` |
| 5 | Bottle Emptier | `Patches/World/SideScreen/MiscBuildingPatches.cs`, `Networking/Packets/World/Handlers/MiscBuildingHandler.cs` – synchronizes the setting and recreates the `FetchChore` with updated source tags |
| 6 | Rockets | `Patches/World/RocketPatches.cs` |
| 7 | Bionics without oil | `Patches/Bionics/BionicPatches.cs` |
| 8 | Alert Notifications | `Patches/Events/NotificationScreenPatch.cs`, `Networking/Packets/Events/NotificationPacket.cs` |
| 9 | Pipe Emptying | `Patches/World/BuildingStatePatches.cs`, `Networking/Packets/World/Handlers/AuthoritativeStateHandler.cs` |
| 10 | Story-trait buildings | `Patches/StateMachines/StateMachineSyncPatches.cs`, `Networking/Components/StateMachineStateSyncer.cs` |
| 11 | Building damage caused by angry duplicants | `Patches/World/BuildingStatePatches.cs`, `Networking/Packets/World/Handlers/AuthoritativeStateHandler.cs` |
| 12 | Creature moods | `Patches/StateMachines/StateMachineSyncPatches.cs`, `Networking/Components/StateMachineStateSyncer.cs` |
| 13 | Coat-Rack-Deconstruction | `Networking/Packets/Tools/Deconstruct/DeconstructCompletePacket.cs` |
| 14 | Complex Fabricator | `Patches/World/Buildings/ComplexFabricator_Patches.cs` |
| 15 | CreatureSimTemperatureTransfer | `Patches/Critters/CreatureTemperaturePatches.cs` |

</details>

- [x] **Rovers are not synchronized** `Issue` `Code fix` `Runtime QA pending`

- [x] **WorldDamage objects (drops) are duplicating** `Issue` `Code fix` `Runtime QA pending`
  > When a tile is destroyed for example, the tile drops a material but also spawns a material from the server

- [x] **Storage deliverables FX on clients says "Picked up 0x <element>" instead of "Delivered"** `Issue` `Code fix` `Runtime QA pending`

- [x] **Loading a save whilst in game crashes the server** `Issue` `Code fix` `Runtime QA pending`

- [x] **Bottle emptier not synchronized** `Issue` `Code fix` `Runtime QA pending`

- [x] **Rockets crash** `Issue` `Code fix` `Runtime QA pending`
  > `Exception in: (HabitatModuleSmall).LaunchableRocketCluster+States.root.grounded. `
  > `System.NullReferenceException: Object reference not set to an instance of an object`
  > `  at LaunchableRocketCluster+StatesInstance.IsNotGroundBound () [0x00015] in <81e516d9c2bc49608bcac24a555d88de>:0 `
  > `  at LaunchableRocketCluster+States+<>c.<InitializeStates>b__6_0 (LaunchableRocketCluster+StatesInstance smi) [0x00000] in <81e516d9c2bc49608bcac24a555d88de>:0 `
  > `  at GameStateMachine`4+State+<>c__DisplayClass118_0[StateMachineType,StateMachineInstanceType,MasterType,DefType].<EnterTransition>b__0 (StateMachineInstanceType smi) [0x00000] in <81e516d9c2bc49608bcac24a555d88de>:0 `
  > `  at StateMachine`4+GenericInstance[StateMachineType,StateMachineInstanceType,MasterType,DefType].ExecuteActions (StateMachine`4+State[StateMachineType,StateMachineInstanceType,MasterType,DefType] state, System.Collections.Generic.List`1[T] actions) [0x00048] in <81e516d9c2bc49608bcac24a555d88de>:0 `
  > `  at LogCatcher.UnityEngine.ILogHandler.LogFormat (UnityEngine.LogType logType, UnityEngine.Object context, System.String format, System.Object[] args) [0x00000] in <81e516d9c2bc49608bcac24a555d88de>:0 `
  > `  at LogCatcher.UnityEngine.ILogHandler.LogFormat (UnityEngine.LogType logType, UnityEngine.Object context, System.String format, System.Object[] args) [0x00000] in <81e516d9c2bc49608bcac24a555d88de>:0 `
  > `  at LogCatcher.UnityEngine.ILogHandler.LogFormat (UnityEngine.LogType logType, UnityEngine.Object context, System.String format, System.Object[] args) [0x00000] in <81e516d9c2bc49608bcac24a555d88de>:0 `
  > `  at LogCatcher.UnityEngine.ILogHandler.LogFormat (UnityEngine.LogType logType, UnityEngine.Object context, System.String format, System.Object[] args) [0x00000] in <81e516d9c2bc49608bcac24a555d88de>:0 `
  > `  at LogCatcher.UnityEngine.ILogHandler.LogFormat (UnityEngine.LogType logType, UnityEngine.Object context, System.String format, System.Object[] args) [0x00000] in <81e516d9c2bc49608bcac24a555d88de>:0 `
  > `  at LogCatcher.UnityEngine.ILogHandler.LogFormat (UnityEngine.LogType logType, UnityEngine.Object context, System.String format, System.Object[] args) [0x00000] in <81e516d9c2bc49608bcac24a555d88de>:0 `
  > `  at LogCatcher.UnityEngine.ILogHandler.LogFormat (UnityEngine.LogType logType, UnityEngine.Object context, System.String format, System.Object[] args) [0x00000] in <81e516d9c2bc49608bcac24a555d88de>:0 `
  > `  at LogCatcher.UnityEngine.ILogHandler.LogFormat (UnityEngine.LogType logType, UnityEngine.Object context, System.String format, System.Object[] args) [0x00000] in <81e516d9c2bc49608bcac24a555d88de>:0 `
  > `  at LogCatcher.UnityEngine.ILogHandler.LogFormat (UnityEngine.LogType logType, UnityEngine.Object context, System.String format, System.Object[] args) [0x00000] in <81e516d9c2bc49608bcac24a555d88de>:0 `
  > `  at LogCatcher.UnityEngine.ILogHandler.LogFormat (UnityEngine.LogType logType, UnityEngine.Object context, System.String format, System.Object[] args) [0x00000] in <81e516d9c2bc49608bcac24a555d88de>:0 `
  > `  at UnityEngine.Logger.Log (UnityEngine.LogType logType, System.Object message, UnityEngine.Object context) [0x00000] in <f54de29543b44ca89695b41bc5ecd6e0>:0 `
  > `  at UnityEngine.Debug.LogError (System.Object message, UnityEngine.Object context) [0x00000] in <f54de29543b44ca89695b41bc5ecd6e0>:0 `
  > `  at Debug.LogError (System.Object obj, UnityEngine.Object context) [0x00000] in <f216bcfdd6e24a84ab7fdf9f59adc3af>:0 `
  > `  at DebugUtil.LogErrorArgs (UnityEngine.Object context, System.Object[] objs) [0x00000] in <f216bcfdd6e24a84ab7fdf9f59adc3af>:0 `
  > `  at DebugUtil.LogException (UnityEngine.Object context, System.String errorMessage, System.Exception e) [0x00000] in <f216bcfdd6e24a84ab7fdf9f59adc3af>:0 `
  > `  at StateMachine`4+GenericInstance[StateMachineType,StateMachineInstanceType,MasterType,DefType].ExecuteActions (StateMachine`4+State[StateMachineType,StateMachineInstanceType,MasterType,DefType] state, System.Collections.Generic.List`1[T] actions) [0x00000] in <81e516d9c2bc49608bcac24a555d88de>:0 `
  > `  at StateMachine`4+GenericInstance[StateMachineType,StateMachineInstanceType,MasterType,DefType].PushState (StateMachine+BaseState state) [0x00000] in <81e516d9c2bc49608bcac24a555d88de>:0 `
  > `  at StateMachine`4+GenericInstance[StateMachineType,StateMachineInstanceType,MasterType,DefType].GoTo (StateMachine+BaseState base_state) [0x00000] in <81e516d9c2bc49608bcac24a555d88de>:0 `
  > `  at StateMachine+Instance.StartSM () [0x00000] in <81e516d9c2bc49608bcac24a555d88de>:0 `
  > `  at StateMachine`4+GenericInstance[StateMachineType,StateMachineInstanceType,MasterType,DefType].StartSM () [0x00000] in <81e516d9c2bc49608bcac24a555d88de>:0 `
  > `  at LaunchableRocketCluster.OnSpawn () [0x00000] in <81e516d9c2bc49608bcac24a555d88de>:0 `
  > `  at KMonoBehaviour.Spawn () [0x00000] in <f216bcfdd6e24a84ab7fdf9f59adc3af>:0 `
  > `  at KMonoBehaviour.Start () [0x00000] in <f216bcfdd6e24a84ab7fdf9f59adc3af>:0 `
  > `Build: U58-722606-SCR`

- [x] **Bionics crash when running out of oil on clients** `Issue` `Code fix` `Runtime QA pending`

- [x] **Alert notifications that play on the host will play their sounds on clients but don't appear in their notification list** `Issue` `Code fix` `Runtime QA pending`

- [x] **Pipe emptying chore not synchronized (anymore)** `Issue` `Code fix` `Runtime QA pending`

- [x] **Story trait buildings not synced** `Issue` `Code fix` `Runtime QA pending`

- [x] **Angry dupes breaking things does not Sync** `Issue` `Code fix` `Runtime QA pending`

- [x] **Animal moods are not synchronized** `Issue` `Code fix` `Runtime QA pending`

- [x] **Crash when deconstructing coat rack** `Issue` `Code fix` `Runtime QA pending`

- [x] **Complex fabricator null reference exception with recipes** `Issue` `Code fix` `Runtime QA pending`
  > NullReferenceException: Object reference not set to an instance of an object ComplexFabricatorSideScreen.HasAllRecipeRequirements (ComplexRecipe recipe) (at <f2341479ee8a413585265ff8b813e609>:0) ComplexFabricatorSideScreen.<RefreshIngredientAvailabilityVis>b\_\_52_0 (ComplexRecipe match) (at <f2341479ee8a413585265ff8b813e609>:0) System.Collections.Generic.List`1[T].Find (System.Predicate`1[T] match) (at <1eb9db207454431c84a47bcd81e79c37>:0) ComplexFabricatorSideScreen.RefreshIngredientAvailabilityVis () (at <f2341479ee8a413585265ff8b813e609>:0) ComplexFabricatorSideScreen.Update () (at <f2341479ee8a413585265ff8b813e609>:0) UnityEngine.DebugLogHandler:LogException(Exception, Object) LogCatcher:UnityEngine.ILogHandler.LogException(Exception, Object) UnityEngine.Debug:CallOverridenDebugHandler(Exception, Object) Build: U59-737195-S\

- [x] **CreatureSimTemperatureTransfer Crash** `Issue` `Code fix` `Runtime QA pending`
  > NullReferenceException: Object reference not set to an instance of an object CreatureSimTemperatureTransfer.unsafeUpdateAverageKiloWattsExchanged (System.Single dt) (at <f2341479ee8a413585265ff8b813e609>:0) CreatureSimTemperatureTransfer.Update () (at <f2341479ee8a413585265ff8b813e609>:0) UnityEngine.DebugLogHandler:LogException(Exception, Object) LogCatcher:UnityEngine.ILogHandler.LogException(Exception, Object) LogCatcher:UnityEngine.ILogHandler.LogException(Exception, Object) UnityEngine.Debug:CallOverridenDebugHandler(Exception, Object) Build: U59-737195-SCRPAN

---

## 📋 List: Work In progress (1)

- [ ] **Auto reconnect if connection to the host is lost**

---

## 📋 List: New features for next update (14)

- [x] **Discord Rich Presence** `Next Update`

- [x] **OxySync framework upgrades** `Next Update` `Internal Testing`
  > **OxySync** is a host-authoritative state-synchronization framework built on top of ONI's `KMonoBehaviour`, modelled after the [Mirror](https://mirror-networking.com/ "‌") networking framework for Unity. It extends the existing ad-hoc packet-based sync system with an attribute-driven pattern that is cleaner, more maintainable, and easier to extend.
  >
  > **What's new**
  > `NetworkBehaviour` — A new base class extending `KMonoBehaviour` that any networked component can inherit from. Handles SyncVar discovery, RPC registration, and dispatch automatically via reflection on `OnSpawn()`.
  > `[SyncVar]` — Marks a field for automatic host→client replication. OxySync samples the field on its configured sync interval, compares it with the last transmitted value, and batches changed fields by interest group and delivery mode. A dirty bit can additionally force a field or the complete component to be resent; ordinary field assignments do not require a generated setter. Supports:
  > - `Hook = "MethodName"` — A callback invoked on the client when the value changes: `void OnFieldChanged(FieldType oldValue, FieldType newValue)`. Invalid hook signatures are reported during discovery and disabled instead of failing during packet dispatch.
  > - `Epsilon = 0.01f` — Minimum magnitude change for floats/vectors to avoid noise
  > - `InterestGroup = id` — Overrides the component's group for this field
  > - `SendMode = mode` — Selects unreliable, reliable, immediate, or no-delay delivery
  > `[Command]` — Client→host RPC. Call via `CallCommand(nameof(Method), args...)`. Optional `RequiresHost = true` makes the command host-local: clients cannot send it, and the host dispatcher rejects it if it arrives through the remote command packet path.
  > `[ClientRpc]` — Host→all-clients broadcast RPC. Call via `CallClientRpc(nameof(Method), args...)`. `IncludeHost = true` also executes the RPC locally on the host, and `InterestGroup` limits the recipients.
  > `[TargetRpc]` — Host→specific-client RPC. Call via `CallTargetRpc(playerId, nameof(Method), args...)`.
  > `[Server]` **/** `[Client]` — Enforced for methods invoked through OxySync RPC dispatch. Unity lifecycle methods such as `Update()` still require an explicit `if (!isServer) return;` or `if (!isClient) return;` guard.
  >
  > **Dispatch and authority model**
  > - The host is authoritative for simulation state. Clients apply replicated state and do not independently advance synchronized building or duplicant state machines.
  > - A single `NetworkIdentity` may own multiple `NetworkBehaviour` components. Incoming SyncVars, Commands, ClientRpcs, and TargetRpcs are resolved against the component that actually declares the field or method; component order no longer causes unrelated updates to be dropped.
  > - SyncVars are intended for persistent state such as battery charge, generator fuel, storage contents, plant statistics, and current state-machine state. RPCs are intended for ordered actions or one-shot events.
  > - Every SyncVar update carries the host timestamp. Clients track it independently per field and reject duplicate or older unreliable packets, preventing a late packet from rolling a battery meter, machine state, or entity property backwards.
  > - Last-sent values are stored as independent serialized snapshots. In-place mutations of arrays and collections are therefore detected instead of being hidden by a shared object reference.
  > - Position, rotation, and scale fields received in one batch share a timestamp. The interpolation buffer merges those fields into one complete snapshot instead of retaining a partially applied transform.
  > - Real-time transforms use timestamped snapshot interpolation and an adaptive jitter buffer. Duplicant positioning deliberately uses this single OxySync path; a second navigation-transition replay path caused conflicting corrections and was removed after runtime testing.
  > - Commands marked `RequiresHost`, RPC direction attributes, method return types, by-reference parameters, supported argument types, SyncVar hooks, and hash collisions are validated before dispatch. Invalid definitions are skipped with an OxySync warning.
  > - Field, RPC-method, and global OxySync identifiers use a deterministic protocol hash instead of runtime-dependent `string.GetHashCode()`, so identifiers remain identical across processes and operating systems.
  > - These identifier and framing changes are handshake protocol version 2. Version 1 peers are rejected before joining instead of connecting with incompatible OxySync IDs.
  > - TargetRpc packets have a dedicated target-method resolver and are also executed directly when the selected target is the local host; they are no longer mistaken for broadcast ClientRpc methods.
  > - RPC payloads are length-framed and bounded to 8 MiB. Truncated payloads, oversized collections, invalid Variant arrays, and incomplete compressed-string reads are rejected rather than partially applied.
  >
  > **Supported values and lifecycle**
  > - RPCs support null reference arguments, numeric primitives, booleans, strings, enums with any integral backing type, vectors, colors, quaternions, byte arrays, hashed strings, nullable values, arrays, and nested `List`, `Dictionary`, `HashSet`, `Queue`, and `Stack` collections made from supported types.
  > - Current SyncVars use OxySync `Variant` values: null references, numeric primitives (including `ulong` and `decimal`), booleans, strings, enums with any integral backing type, vectors, colors, quaternions, byte arrays, hashed strings, and supported arrays/collections. Unsupported values now fail explicitly instead of silently becoming integer zero.
  > - Call `base.OnSpawn()` so discovery and manager registration run, and call `base.OnCleanUp()` so the behaviour is removed from the sync and interest-group indexes.
  > - Every synchronized GameObject needs one registered `NetworkIdentity`. Multiple OxySync behaviours may safely share that identity.
  >
  > **Example**
  > ```csharp
  > public sealed class ExampleSyncer : NetworkBehaviour
  > {
  >     [SyncVar(Hook = nameof(OnValueChanged), Epsilon = 0.01f)]
  >     private float value;
  >
  >     [Command]
  >     private void CmdSetValue(float requested)
  >     {
  >         value = requested; // executed by the authoritative host
  >     }
  >
  >     [ClientRpc]
  >     private void RpcPlayEffect(string effectId) { }
  >
  >     private void OnValueChanged(float oldValue, float newValue) { }
  > }
  > ```
  > Persistent fields are sampled on their configured `SyncInterval`; latency-sensitive movement uses the no-delay unreliable channel, while final stop states and state-machine changes use reliable delivery. Manual dirty tracking is now dynamically sized, so components are not limited to 32 explicitly dirtied SyncVars; the legacy 32-bit accessor remains available for API compatibility.

- [x] **Network Overlay [SHIFT + F5]** `Next Update`
  > ![image.webp](https://trello.com/1/cards/6a4680c184cad11423bf070b/attachments/6a46880f06374b026789a70f/previews/6a46881106374b026789a720/download/image.webp)

- [x] **OxySync: Network Interest Groups** `Next Update`
  > Network interest groups are a way to control which clients receive which network updates, based on what is actually relevant to them.
  > Instead of broadcasting every network packet to every client, the server assigns each NetworkedBehaviour (or world region) to an “interest group.” Clients only receive updates for the groups they are currently interested in.
  > For example, imagine a colony with multiple planetoids:
  > - Client A is currently on **Planetoid A**
  > - Client B is currently on **Planetoid B**
  > All NetworkBehaviours on Planetoid A (dupes, buildings etc.) are assigned to the **Planetoid A interest group**. Likewise, Planetoid B has its own group.
  > So when the server sends updates:
  > - Client A receives only packets for Planetoid A
  > - Client B receives only packets for Planetoid B
  > This means:
  > - Position updates
  > - Structure changes etc
  > …are only sent if the client is in the same interest group as the NetworkBehaviour.
  > As a result, Client B will not receive any network traffic for Planetoid A, reducing bandwidth and preventing unnecessary processing.

- [x] **OxySync: Network Transform (Position, Rotation, Scale)** `Next Update`

- [x] **OxySync: Storage sync updated** `Next Update`

- [x] **OxySync: Plant sync updated** `Next Update`

- [x] **OxySync: Nuclear reactor sync updated** `Next Update`

- [x] **OxySync: Generator sync updated** `Next Update`

- [x] **OxySync: Battery sync updated** `Next Update`

- [x] **OxySync: Toilets / Outhouses sync updated** `Next Update`

- [x] **OxySync: State Machine Syncer (Clinic, Food)** `Next Update`

- [x] **OxySync: Chatbox** `Next Update`

- [x] **Chatbox UI Updated to fit ingame UI better.** `Next Update`

---

## 📋 List: Bugfixes for next update (10)

- [x] **Fixed entities added in Aqua Planet DLC being desynced (Crabs etc)** `Next Update`

- [x] **Fixed Printing Pod not changing selections** `Next Update`

- [x] **Fixed Printing Pod Meter not being synchronized** `Next Update`

- [x] **Fixed Batteries falling out of sync** `Next Update`

- [x] **Fixed generators falling out of sync** `Next Update`

- [x] **Fixed storage containers falling out of sync** `Next Update`

- [x] **Fixed Asteroid Cycle Clock (Top-Left) teleporting and stuttering** `Next Update` `Code fix`
  > Allowed continuous local time progression on clients via `GameClock.AddTime` and implemented smooth interpolation (Lerp) for periodic time sync packets (`GameTimeSyncer` and `WorldCyclePacket`) to prevent the asteroid cycle clock from freezing and teleporting every second.

- [x] **Fixed Duplicant Eating Animations not playing on clients** `Next Update` `Code fix`
  > Added `Edible` to `workablesToSkip` in `StandardWorker_Patches` and protected minion/creature animation playback in `KAnimControllerBase_Patches` so eating animations and hand overrides replicate without being overwritten by the client's local idle state.

- [x] **Fixed Plant Harvest and Seed Item Drops missing on clients** `Next Update` `Code fix`
  > Added `ScenarioSpawnPrefabPatch` to capture all items spawned by `Scenario.SpawnPrefab` (harvested food, dropped seeds, loot) on the host and replicate them with synchronized `NetworkIdentity` and `SpawnPrefabPacket` to clients.

- [x] **Full Multi-Language Localization (German & Polish)** `Next Update` `Code fix`
  > Added full 341-key PO files for German (`de.po`) and Polish (`pl.po`), enhanced culture code resolution in `LocalizationPatch`, and added dynamic UI translation in `UnityMultiplayerScreen`.

---

## 📋 List: Synced (Live and next update) (60)

- [x] **Dupes skills sync** `Live`

- [x] **Dupe Priorities menu** `Live`

- [x] **Duplicant (Bionic and custom modded duplicants) Movement** `Live`

- [x] **Critter Animation** `Live`

- [x] **Chore / Errand list sync** `Live`

- [x] **Dig Tool** `Live`

- [x] **Cancel Tool** `Live`

- [x] **Prioritize tool** `Live`

- [x] **Build Tool** `Live`

- [x] **Multi planetoid build tool sync (Spaced out)** `Live`

- [x] **Play / Pause / Speeds** `Live`

- [x] **Research** `Live`

- [x] **Schedule Sync** `Live`

- [x] **Consumable per duplicant permissions** `Live`

- [x] **Building placement** `Live`

- [x] **Direct world transfer** `Live`

- [x] **Hard sync** `Live`

- [x] **Wires/Water pipes/Gas pipes** `Live`

- [x] **Clear Tool** `Live`

- [x] **Deconstruct Tool** `Live`

- [x] **Falling Objects** `Live`

- [x] **[Host Authoritative] World Cycle** `Live`

- [x] **[Host Authoritative] World State (Gas, Liquid)** `Live`

- [x] **Most of (If not all) Buildings** `Live`
  > Almost sure that this is every building in the game, if missing any, please comment.
  > If any buildings don't sync any option, comment too.
  > Or make a bug report on the discord

- [x] **[FilteredDragTools] Tool Filters** `Live`

- [x] **Snipping Tool** `Live`

- [x] **Empty Pipe Tool** `Live`

- [x] **UtilityBuild Tool (Wire / Pipe etc connections)** `Live`

- [x] **Move To Tool** `Live`

- [x] **Attack Tool** `Live`

- [x] **Capture Tool** `Live`

- [x] **Copy Settings Tool** `Live`

- [x] **Harvest Tool** `Live`

- [x] **Critter Positions** `Live`

- [x] **[Building] Printing Pod** `Live`

- [x] **Bionics and other custom modded duplicant types** `Live`

- [x] **Duplicant (Bionic and custom modded duplicants) Animations** `Live`

- [x] **Duplicants (Bionics and other custom modded duplicant types) effects** `Live`

- [x] **Complex Fabricator buildings spawning of products** `Live`

- [x] **Custom name (Duplicants, buildings etc)** `Live`

- [x] **Operational building states** `Live`

- [x] **Progress bars are now synced** `Live`

- [x] **Workable progress** `Live`

- [x] **Battery sync** `Live`

- [x] **Mod Menu Options** `Live`

- [x] **Generator Meter Sync** `Live`

- [x] **See another players desired preview building (before they place it)** `Live`
  > Not really a requested feature but its something I thought of after seeing this.
  > ![image.webp](https://trello.com/1/cards/6a01c2f792a4e3624ff18908/attachments/6a021d8c360bdd36adafb9aa/previews/6a021d8d360bdd36adafbc2b/download/image.webp)
  > ![image.webp](https://trello.com/1/cards/6a01c2f792a4e3624ff18908/attachments/6a021d85845e4765f86476d7/previews/6a021d85845e4765f86477b3/download/image.webp)
  > [https://youtu.be/5PzYGg4Qlc4?t=605](https://youtu.be/5PzYGg4Qlc4?t=605 "smartCard-embed")

- [x] **Storage Elements are now synchronized** `Live`
  > StorageLockers, Rationboxes, Cargoboxes

- [x] **Pipe elements are now synchronized. See what is flowing through pipes** `Live`

- [x] **Side Screens should now refresh for players also looking at the building (For example, storage bins)** `Live`

- [x] **See other players drag tools areas** `Live`

- [x] **Network Optimization: Duplicants and Critters will now only send positional packets when they are actively being observed by a player** `Live`
  > ‌
  > [https://www.youtube.com/watch?v=HvMJeUdnFL8](https://www.youtube.com/watch?v=HvMJeUdnFL8 "smartCard-embed")

- [x] **Red Alert synchronized** `Live`

- [x] **Mod Options: Packet Queue** `Live`

- [x] **Mod Options: Max Packets Per Second** `Live`

- [x] **Mod Options: Bypass Protocol** `Live`

- [x] **Thought / Chitchat bubbles synchronized** `Live`

- [x] **Duplicant Status' synchronized** `Live`

- [x] **Dream bubbles synchronized** `Live`

- [x] **Duplicant carried items synchronized** `Live`

---

## 📋 List: Synched with Issues (Live and next update) (10)

- [ ] **Consumable for new duplicants** `Live`

- [ ] **Vital tab Stats** `Live`

- [ ] **Resource Display** `Live`

- [ ] **Mopping Tool** `Live`

- [x] **Disinfect Tool** `Live` `Code fix`
  > Disinfecting progress and visual beams/multitools are now reliably cleared on clients upon work completion or cancellation via `Workable.CompleteWork` / `Workable.StopWork` and `RemoteProgressRegistry.HideTarget()`.

- [ ] **Gas/Liquid on world** `Live`

- [ ] **Notifications (In-game events)** `Live`

- [ ] **Diagonistics** `Live`

- [x] **Plant sync (Plants don't drop their harvested loot on clients)** `Live` `Code fix`
  > Fixed via `ScenarioSpawnPrefabPatch`: when plants are harvested or uprooted on the host, fruit, seeds, and dropped loot spawned through `Scenario.SpawnPrefab` are registered with `NetworkIdentity` and broadcast to all clients via `SpawnPrefabPacket`.

- [x] **Pickupables (sweeping etc) synced (Objects don't appear on dupes back on clients right now)** `Live` `Code fix`
  > Fixed via `PickupablePatches` partial pickup protection and `DuplicantCarryItemPacket`. Partial pickups no longer destroy the world item stack prematurely on clients, and carried items render accurately on duplicant backs.

---

## 📋 List: Complete (32)

- [x] **Networking framework (Lobby, inviting, joining, packets etc)** `Live`

- [x] **Steam overlay support** `Live`

- [x] **Lost connection to host screen** `Live`

- [x] **Ingame Chat** `Live`

- [x] **Everything in Synced Tabs** `Live`

- [x] **[DEBUG] DevTool for Multiplayer** `Live`

- [x] **Player Cursors** `Live`

- [x] **Client / Host Ready Screen** `Live`

- [x] **Manual Hard Sync** `Live`

- [x] **Network Statistics** `Live`

- [x] **API for modder integrations** `Live`

- [x] **DLC Check** `Live`

- [x] **Server browser** `Live`

- [x] **Password protected servers** `Live`

- [x] **Lobby codes** `Live`

- [x] **Full DLC support** `Live`
  > ![image.png](https://trello.com/1/cards/695a7834cad0335cc654ffa2/attachments/695fb46dcfa70637118445ab/download/image.png)

- [x] **Support for The Aquatic Planet Pack DLC** `Live`
  > ![image.webp](https://trello.com/1/cards/6a119cd9fe428283a6e8cbb8/attachments/6a119d69d6a2613999495553/previews/6a119d6ad6a2613999495564/download/image.webp)

- [x] **Client Mod Validation Layer** `Live`
  > You’ll need to have matching mods to connect with eachother

- [x] **TCP-like file transfer system (World Saves, supports large save file transfers)** `Live`

- [x] **Overhaul UI to be unity objects and themed to match the game** `Live`

- [x] **Network Indicators (Packet loss, Latency, Jitter, Server Performance)** `Live`

- [x] **New version detection** `Live`
  > ![image.png](https://trello.com/1/cards/696c4691ebb94ecaa088b1b2/attachments/696c5124bcb8774a9912c5d0/download/image.png)

- [x] **Ping System** `Live`

- [x] **Remade the network architecture into an abstract network layer to enable multiple network transports besides Steamworks** `Live`

- [x] **Direct Connect / Lan (IP:Port) (Riptide)** `Live`
  > Lan has been successfully implemented using the Riptide framework: [https://github.com/RiptideNetworking/Riptide](https://github.com/RiptideNetworking/Riptide "smartCard-inline")
  > ‌
  > Tested and working with Radmin, Hamachi, Tailscale etc
  > ![image.png](https://trello.com/1/cards/694d3ffd302ae6791e4d3e87/attachments/696e6e449776ba00da8bfd98/download/image.png)

- [x] **When using LAN, save file is transferred Via TCP instead of UDP. This means save files are transferred quickly** `Live`
  > When hosting on LAN. The TCP server runs on <ip>:<port+1>
  > ‌
  > So for example:
  > If you were running on local host with a port of 7777
  > The game server would be running on 127.0.0.1:7777
  > but the TCP server will be running on 127.0.0.1:7778
  > ‌
  > Works with Radmin, Hamachi, Tailscale etc

- [x] **Contributors: New ImGui profiler added to the DevTool in DEBUG builds** `Live`
  > ![image.webp](https://trello.com/1/cards/69c2dee9b6ff64b40680f8d4/attachments/69c405967591cde970fa2989/previews/69c405977591cde970fa2a91/download/image.webp)

- [x] **Contributors: New ImGui Unit Test framework** `Live`
  > ![image.webp](https://trello.com/1/cards/69d67f2512233d605d691f9c/attachments/69d67f318ee2da917247d052/previews/69d67f318ee2da917247d063/download/image.webp)

- [x] **Network Protocols** `Live`
  > Mismatching versions of the mod will no longer be able to play together etc

- [x] **Additional Lobby Settings is now functional** `Live`

- [x] **New Lobby Setting: Hard Sync on Cycle Start** `Live`

- [x] **Mod Options: Change the connection timeouts for client and host** `Live`
  > Increase the timeouts if you’re having connection issues or take a while to load in

---

## 📋 List: Benched (For now) (2)

- [ ] **EXPERIMENTAL: Dedicated Servers** `Benched`
  > This is essentially a listening server, it doesn't run the simulation. It listens for network traffic and relays it to the clients.
  > It will need to be informed about things like the save file etc.
  > A save file is uploaded by the host, a client connects and downloads that save file, the first client is considered the master and their state is what overwrites the dedi save
  > If a save action happens on the master, upload it to the dedi, if the master disconnects with clients present, the next client sends the save state to the dedi and it overwrites it with that one
  > This is purely conceptual
  > Maybe it'll be better to hold the save file in Memory and use that then only save locally if the server shuts down
  > ![image.png](https://trello.com/1/cards/6970bd9c684885c3bc487e0e/attachments/6970bdbe8fc246c734af8d05/download/image.png)

- [ ] **Epic Online Services Network Transport** `Benched`

---
