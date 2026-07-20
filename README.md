# Oxygen Not Included Together

Oxygen Not Included Together is a synchronized multiplayer mod for Oxygen Not Included. Version 1.0.5 targets game build `U59-740622-S` and uses network protocol version `9`.

This repository is a personal development fork of [Lyraedan/Oxygen_Not_Included_Together](https://github.com/Lyraedan/Oxygen_Not_Included_Together). The original repository is no longer maintained. This fork is independent and unofficial.

## Status

- The base game and Spaced Out! have completed two-machine runtime validation.
- Frosty Planet Pack, Bionic Booster Pack, Prehistoric Planet Pack, Neutronium Cosmetics Pack, and Aquatic Planet Pack have dedicated synchronization code and compile in the release build.
- Clients submit building, schedule, priority, skill, assignment, and DLC interaction requests to the host. The host validates each request, updates the colony, and broadcasts the resulting state.
- Joining and reconnecting clients load a generation-bound full snapshot before reliable live updates resume.
- Steam play uses friends-only lobbies over SteamNetworkingSockets. Players can join by lobby code or Steam invite without port forwarding, a public IP address, or a LAN tunnel.
- LAN play uses Riptide UDP. Large saves use the adjacent TCP port, with chunked UDP transfer as a fallback.
- The handshake admits a peer when its `ONI_Together.dll` SHA-256 and active DLC set exactly match the host.
- The dedicated-server prototype remains in the repository but is not included in the Workshop release.

## Installation

See [INSTALL.md](INSTALL.md) for Steam Workshop installation, source builds, LAN ports, and release packaging.

Do not enable a Workshop copy and a local source build at the same time. Every player must use an `ONI_Together.dll` with the same SHA-256 and enable the same DLC set. A rejected client now sees the specific DLL or DLC mismatch instead of a generic connection-loss message. Other enabled Mods, load order, and configuration are not admission checks.

## Usage

1. Enable `Oxygen Not Included Together` for the active DLC on the Mods screen and restart the game.
2. Open Multiplayer from the main menu.
3. For Steam play, the host creates a lobby and shares its code or sends a Steam invite. Each player runs the game from a separate Steam account.
4. For LAN play, the host listens on UDP `8080` by default. Save transfer uses TCP `8081`.

SteamNetworkingSockets handles NAT traversal and relay selection for Steam sessions. Steam play does not use the LAN address or require a port-forwarding rule. Tunnels are only relevant to the separate direct-LAN transport.

## Synchronization model

The host owns authoritative colony state. A joining client receives a full snapshot tied to the current session generation. Reliable changes produced during loading are retained until the client confirms that the snapshot is loaded.

Runtime validation splits state into five domains: `grid`, `entity`, `storage`, `world`, and `rocket`. Each validation segment records the raw hashes, applies a host keyframe, waits for its acknowledgement, and compares the post-keyframe hashes.

Entity lifecycle updates use monotonic revisions and tombstones. Failed identity claims restore the previous NetId, lifecycle journal, position, and active state instead of leaving a partial binding.

## Validation record

On July 20, 2026, the v1.0.5 code candidate completed 554 in-game Debug checks on both the macOS host and an Alienware Windows client: 528 passed, none failed, and 26 were skipped because the required runtime state was not present. The Windows client then downloaded the host save, applied all 874 world-baseline parts, and entered `InGame` after Ready acknowledgement 1.

The two-machine Steam friends soak used two Steam accounts and ran for 21 segments and 37,800 ticks with ONI MCP Server disabled. Time and all five post-keyframe domain hashes matched in every segment. The final record reported `postMismatchSeen=False`, `keyframeApplyFailureSeen=False`, and `postKeyframeEqual=True`; lifecycle missing, unexpected, tombstoned-live, and unassigned counts were all zero.

The client was then closed, restarted through Steam, and joined the same lobby code. It reapplied all 1,040 world-baseline parts, entered `InGame`, and completed reconnect setup after Ready acknowledgement 2.

Raw drift appeared in `grid`, `entity`, `world`, and `storage` at the first segment. This mod uses host-authoritative repair rather than deterministic lockstep. A post-keyframe mismatch is a release failure.

## Development

Debug builds expose three in-game entry points:

- `Shift+F2` opens the test menu.
- `Shift+F3` discovers and runs all in-game unit tests.
- `Shift+F4` runs the Riptide loopback smoke test on `127.0.0.1:27777`.

The project targets `netstandard2.1`. One DLL is used on macOS, Windows, and Linux, while the release directory includes the UI asset bundle for each platform.

## License

ONI Together is licensed under the [MIT License](LICENSE.md). Original project credit and third-party notices are preserved in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
