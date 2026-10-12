# Client colony refresh

A connected client that has finished loading can open **Multiplayer** and press **Refresh My Colony**. The dialog shows progress and the result, and can be closed while the refresh continues. Opening this dialog on a client does not pause the simulation. Hosts retain their existing hard-sync button.

This is a rolling refresh of terrain across the whole grid (including other loaded planetoids), existing supported structure components, tracked automation, and their stored contents. It leaves the connection active. Missing entities, construction changes, and unsupported object internals are outside its scope and can still require hard sync. All participants need this build: the protocol version is now **2**, with the packet-fingerprint compatibility check retained.

Only one host job runs at a time. Other clients receive a busy result; each accepted requester has a 30-second cooldown. A request fails after 30 seconds without progress. Applied records are acknowledged before completion; skipped or failed records produce an incomplete result. Disconnecting, unloading the world, shutting down the session, or starting hard sync cancels the job.

Transfer uses 32×32 terrain regions, batches under 64 KiB, and at most one outstanding batch. Individual records can be fragmented up to 4 MiB. Capture and application yield cooperatively around a 2 ms frame budget, with limits of 1,024 terrain cells or eight building work records. Individual serialization, parsing, and Unity operations can exceed that budget; in-game performance still needs measurement.

Host revisions are stamped when state is captured or queued. Both ordinary synchronization and refresh application reject older revisions, so a delayed refresh cannot replace a newer received update. Targeted sampling leaves normal broadcast caches and viewport subscriptions unchanged.

Storage preservation rules are adapted from [PR #175 by Kyle Yi (younatics)](https://github.com/Lyraedan/Oxygen_Not_Included_Together/pull/175). Assigned objects and critters are excluded from the blob and preserved on the receiver. Ordinary stacks match by prefab in stable container order, keeping existing identities and additional item state. The entire blob and its prefab references are validated before container mutation. Missing ordinary stacks are inserted through the container's deserialization path, which avoids automatic stack absorption; obsolete ordinary stacks are removed. Toilets and reactors use the same reconciliation helper through their existing syncers.

## Installing a test package

Extract the package's `ONI_Together_dev` folder into your Klei `OxygenNotIncluded/mods/dev` directory, or copy it from the build's staging directory. Enable this build in the game's Mods screen and restart. Use one enabled copy of ONI Together on each participant's machine. The ZIP contains the DLL, mod metadata, and platform UI assets.

## Isolated validation

Build the solution against your installed game libraries, then run the standalone harness. It requires .NET 8 or newer and does not start Unity. From the repository root, in PowerShell:

```powershell
$gameLibraries = 'D:/Games/Oxygen Not Included/OxygenNotIncluded_Data/Managed'
$modStage = Join-Path $PWD 'dev_build/client-colony-resync/mods'
$env:DOTNET_ROLL_FORWARD = 'Major' # For the repository's .NET 6 publicizer tool if needed.
dotnet build ONI_Together.sln "-p:GameLibsFolder=$gameLibraries" "-p:ModFolder=$modStage"
dotnet run --project tests/ColonyRefresh.Tests -- "$PWD" "$gameLibraries"
```

The 19 tests cover grid edges, fragmentation and batch wire limits, acknowledgement routing and duplicates, cooldown/busy behavior, cancellation, timeouts, sender isolation during transport reassembly, response correlation, revisions and state packet roundtrips, strict payload parsing, and storage matching/validation. They do not exercise Unity container mutation, critter/suit preservation, or actual transport connections.

## Live-game checklist — pending

Repeat these checks with Steam and LAN, using one host and two clients running the same build:

- Load a test colony with supported buildings, automation, stores, and at least two loaded planetoids. Start the simulation and have client A request a refresh. Confirm the host and client B keep playing, and only A receives refresh batches. Transport packet tracking can distinguish `ColonyRefreshBatchPacket` from ordinary broadcast traffic.
- Close and reopen A's dialog during the transfer. Progress must continue without a loading overlay, retained screen callback, disconnect, or simulation pause.
- Check off-screen terrain, the last grid rows/columns, and a separate planetoid. Compare batteries, tracked automation, food/material storage, toilet storage, and reactor supply/reaction/waste storage with the host.
- Refresh twice. Check matched stored objects keep their network identity and extra state; quantities, temperature, and disease agree with the sampled host state. Suits retain assignment, oxygen, and durability; trapped critters retain identity and life state, with no duplicates.
- Request simultaneously from A and B. One is accepted and the other receives busy. Check the requester's cooldown and that B can request after A finishes. Interrupt a transfer by disconnect, scene change, session shutdown, and hard sync; delayed packets must not restart it.
- Under a debugger or controlled packet delay, deliver an older refresh record after a newer ordinary update for the same cell/component. The newer received state must remain. Withhold acknowledgements for 30 seconds and verify failure releases the host job. Inspect incomplete results for missing or oversized supported records.

No live-game or multiplayer results should be marked passed until actually performed.
