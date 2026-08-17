# Field notes from a two-PC LAN test rig

Notes for anyone developing this mod, contributed as reference rather than as a change to
behaviour. Nothing here asks you to adopt a workflow. Every entry is something that was
measured on two Windows machines running a real host/client session, with the measurement
that established it, so it can be checked or discarded on its own.

Where an earlier explanation of ours turned out to be wrong, it is listed as wrong. Those
are in [What we disproved](#what-we-disproved), and they are probably the most useful part -
they are things nobody needs to chase.

Fixes that came out of this are already open as PRs #170-#178. There is a list at the end.

---

## The two peers are not running the same thing

This is the premise everything else rests on, and it is easy to forget while debugging.

**The client's AI is switched off.** `ChoreConsumer`, `MinionBrain` and `CreatureBrain` are
disabled and `AdvancePath` is blocked. So a divergence is almost never "the two simulations
drifted apart on their own" - it is an event that was replicated once, twice, or not at all.

Two consequences that cost real time before they were written down:

- **The client is not a control for host performance.** Host at 82 ms and client at 17 ms
  reads like a hosting cost and is not one; they are not simulating the same work. The real
  control is the same colony with the session stopped. Measured that way, the whole
  multiplayer cost was about 1 ms per frame, and roughly 70% of the host frame was ONI's own
  chore and pathfinding.
- **Anything a duplicant has to walk to will never complete on a client.** A state change
  implemented as a chore is a state change the client cannot reach. `QueueStateChange` for
  doors is one; the suit locker is another. On a client the completion function has to be
  called directly.

**Transport asymmetry.** Steam allows ~1200 bytes; Riptide (UDP) allows 1000 and splits the
excess into `ChunkedPacket`. Several syncers carry a comment about fitting Steam's MTU and
then build 1100-byte packets, so those fragment **on LAN only**. That 200-byte gap sits under
more than one bug.

---

## Game APIs that do not behave the way their names suggest

All of these were read out of `Assembly-CSharp` rather than inferred. Guessing at names in
this codebase has a poor record - one function took three failed compiles, and one guess
killed a client twice.

**`SuitLocker.UnequipFrom` has no null check.**

```csharp
Assignable assignable = equipment.GetAssignable(Db.Get().AssignableSlots.Suit);
assignable.Unassign();
```

The game only reaches it after the chore that put the suit on, so the first line is never
null there. Replayed on a client whose duplicant is wearing nothing, the second line throws.
Measured: five calls, five `NullReferenceException`s, in two runs.

**`SuitLocker.EquipTo` declines silently.** It opens with `GetStoredOutfit()` and returns
without doing anything when the locker is empty. Calling it blind does no damage and also
does nothing - so "it did not throw" is not evidence that a suit moved.

**`BuildingHP.Repair` fires a trigger that a field write does not.**

```csharp
hitpoints = Math.Min(hitpoints + repair_amount, max);
Trigger(-1699355994);                        // changed
if (hitpoints >= max) Trigger(-1735440190);  // fully repaired  <- this ends the errand
```

The game's damage event only subtracts, so a repair arriving as a negative delta is declined,
and it is tempting to write the field instead. Then the number is right and the repair errand
is not: `hp DIFFERENT 0` while the client still shows repair work on wires the host has
finished. Two or three rows every run, and they disappear when `Repair` is called instead.

**`PlantablePlot.SpawnOccupyingObject` tells you whether something was planted.** For a
`PlantableSeed` it instantiates the plant and returns *that*; for anything else it sets
`destroyEntityOnDeposit = false` and returns the deposited object unchanged. So "a different
object came back" is the test, and it needs no species list, no tag and no component. Two
attempts to answer the same question with `Growing` and then `GameTags.Plant` were both
wrong - see below.

**`SingleEntityReceptacle.ForceDeposit`** is the entry point for sowing: it clears the
occupant, calls `SpawnOccupyingObject`, configures and positions the result, and destroys the
seed. A test rig does not need to place a plant by hand.

**`Util.KInstantiate` is not different from doing it by hand.** It instantiates, copies tags
and calls `RunInstantiateFn` - the same sequence `InstantiationsPacket` already performs
inline. Neither registers `Grid.Objects`; `OccupyArea.OnSpawn` does that when the object is
activated, which both paths do. The only difference is the scene-layer Z.

**`EquippableWorkable.RefreshChore` is the reconciler for an item's own errand.** It cancels
any existing chore and creates one only when the owner is not already wearing the item.
Worth knowing because assignment replicates before an item does, so on a client it runs while
the duplicant is still empty-handed.

---

## Shapes in this codebase that produced defects

Not criticism - each of these is a reasonable design that has one sharp edge, and the edge is
what to watch for.

**A receiver that reconciles by absence will believe an empty snapshot.** The plant sweep
destroys any plant it holds that the packet does not list. Batching a snapshot across packets
without a sweep id therefore has each batch delete the plants belonging to the others; that
deleted 294 of a client's plants. `PlantGrowthStatePacket` now carries `SweepId` /
`BatchIndex` / `BatchCount` and refuses an empty sweep while holding plants.

**Deltas with no keyframe cannot recover from a missed delta.** Every syncer built on
`StructureSyncerBase` had this shape. Automation signals were the clearest case: a logic
state that was missed once stayed wrong for the session. A 15-second keyframe closed it
(`logicResyncs` 323 on the host, 0 on the client - the asymmetry is the proof it fired).

**`NetworkIdentity.NetId` is `[Serialize]`d.** A save restores its old ids and never calls
the hash, so only freshly spawned objects exercise the id path at all. A test that loads a
save and looks around cannot see id bugs; something has to be dug, built or dropped.

**A registry clear that does not tell the components.** `Clear()` emptied its dictionary and
left `NetId` and `IsRegistered = true` on 8000 objects, while `RegisterIdentity()` starts with
`if (IsRegistered && NetId != 0) return;`. Registration only happens in `OnSpawn`, and a
reconnect spawns nothing - so after a reconnect the world believed it was registered and the
registry knew nothing. Measured on the client: `registry 8085 -> 41`, lookup failures
`4,586 -> 90,068`, shared ids `8105 -> 31`, while the world itself was intact and the
connection looked perfectly healthy. Nothing in the UI pointed at it.

**A guard that only prevents the exception still sends the unset value.**

```csharp
if (ToolMenu.Instance?.PriorityScreen != null)
    Priority = ...GetLastSelectedPriority();
...
writer.Write(Priority.priority_value);   // the unset 0 goes out anyway
```

Six packets had this. The receiver pushed the 0 into its own priority screen and ran the tool:
`Priority Value Out Of Range: 0`. A value on the wire wants a valid default, not a skipped
assignment.

**Counters placed after a decision cannot report the decision.** A branch that declines and
returns, with the counter below it, reads exactly like a branch that never ran. This is worth
a habit: when a condition is widened, add the decline counter in the same change. One gate
that counted itself named a failure in a single run that had otherwise been invisible for
three.

---

## What we disproved

Time spent so nobody spends it again.

**"`Object.Instantiate` does not register `Grid.Objects`, which is why a replicated plant ends
up nowhere."** Wrong, and it was our own written explanation for two reverted attempts. A
probe built a plant on the client at a real cell and `cell-dump` found it in
`Grid.Objects[cell, Building]` on **both** peers. Registration happens on activation. What
actually blocked plant replication was that every path required a `Growing` component.

**"Tiles go missing."** No. That came from comparing `[NETID]` dumps, which only list objects
that have an id - the tile was on the client with no NetId. Counted by cell: 1,409 against
1,409, difference 0. Ids are the thing under investigation in this mod; they are not a safe
axis for asking whether an object exists.

**"`Growing` identifies a plant."** No. `ExtendEntityToBasicPlant` adds `Growing` only to
plants that grow a crop. A Wheezewort has none - and a Wheezewort is exactly the plant that
diverges. `PlantTracker.AllPlants` is a `HashSet<Growing>`, so the plant count in a health row
cannot see one either. Three instruments read as though nothing was wrong.

**"`GameTags.Plant` identifies a plant."** Also no, which was the second guess.
`ColdBreatherConfig` builds its prefab with `CreatePlacedEntity` and never calls
`ExtendEntityToBasicPlant`, so it carries no plant tag at all.

**"The client is missing objects the host has."** Mostly not. Of 41 ids a client gave up
resolving, 30 were loose gas and liquid piles it had received and then merged away itself -
Oxygen, DirtyWater, Water, Methane, CarbonDioxide, Hydrogen, Dirt. No building, no duplicant.
Both simulations merge nearby piles and do not always keep the same object: mass and
temperature agree, the surviving object does not. Player-invisible, and a local rule cannot
fix it, because the peers are not merging the same *pairs*.

**"The client is missing suit equips."** No. A keyframe restating who wears which suit every
15 seconds applied 0 of 118 - the client already agreed at every check, reconnects included.

**"Reconnect never succeeds."** It does now, but the interesting part is that a unit test for
the known cause existed and passed the whole time it was broken: it checked the reconnect
address and nothing about what happened afterwards. Only a live reconnect found the registry
problem above.

---

## Fixes so far

Open as PRs #170-#178 against `testing/network-backend-upgrades`, each with the measurement
in its description.

| | What |
|---|---|
| #170 | Handshake does not compare mod versions, so a mismatched pair produces the symptoms of the bugs being investigated |
| #171 | Off-screen conduit contents were not replicated - divergence appeared only in the 356 cells that were not sent |
| #172 | Battery tracker ran during load |
| #173 | `SpawnPrefabPacket` had no parameterless constructor, so every one threw on receipt |
| #174 | A building found by cell was being renamed |
| #175 | Container sync destroyed suits and critters |
| #176 | `NetworkIdentity` lookups that never attach |
| #177 | Automation state had no periodic resend |
| #178 | Runtime census comparing the two peers |

Landed in the fork and not yet proposed here, listed so you know what exists:

- **Client crashes**, three separate causes: dig notifications, `BalloonStand`, animation
  overrides. Client error count has stayed at 0 since.
- **Electrical circuits**: 474 divergent rows to 0-1. Two causes - a test tool passing
  `(UtilityConnections)0` to `FinishConstruction`, and three genuinely unconnected wires.
- **Resource amounts**: five unrelated defects behind one symptom. Individually each looked
  like no improvement (96 -> 92 -> 99 divergent rows); together, 99 -> 6.
- **Sweep marks, item priorities**: carried on the census. A mark cleared when a duplicant
  collects debris is not a tool action, so nothing replicated it and the client kept showing
  work that was done.
- **Suits**: the event, replayed through `EquipTo` / `UnequipFrom` with the game's own
  preconditions asked first. Divergent rows 11 across 9 runs to 0 across 4.
- **Plants sown during a session**: host 19 cells against client 18, the same cell missing
  every run, now 19 and 19.
- **Repair errands**: through `BuildingHP.Repair` rather than a field write, so the errand
  ends with the repair.

---

## If you want to reproduce any of it

The fork has a two-machine harness: it loads a save on both boxes, hosts, joins, digs,
builds, deconstructs, damages, sows, optionally reconnects, settles, runs an in-game test
suite on both peers and diffs the two logs. A run is about eight minutes unattended and ends
with a pass or fail rather than an opinion.

Also there, and smaller and possibly more useful: a `testing/decomp` utility that prints any
game method's body out of the publicised assembly, which is where most of the API notes above
came from.

**https://github.com/younatics/Oxygen_Not_Included_Together** (branch `mp-stability`)

Some of that documentation is in Korean; happy to translate any part of it, or to open PRs for
the harness if it would be welcome. The measurements above are the part that stands on its own,
which is why they are here and the tooling is not.
