# Oxygen Not Included Together

Oxygen Not Included Together is a synchronized multiplayer mod for Oxygen Not Included. Version 1.0.0 targets game build `U59-740622-S` and uses network protocol version `9`.

This fork is the maintained source for the current release:

https://github.com/Ericbai06/Oxygen_Not_Included_Together

## Status

- The base game and Spaced Out! have completed two-machine runtime validation.
- Frosty Planet Pack, Bionic Booster Pack, Prehistoric Planet Pack, Neutronium Cosmetics Pack, and Aquatic Planet Pack have dedicated synchronization code and compile in the release build.
- Clients submit building, schedule, priority, skill, assignment, and DLC interaction requests to the host. The host validates each request, updates the colony, and broadcasts the resulting state.
- Joining and reconnecting clients load a generation-bound full snapshot before reliable live updates resume.
- LAN play uses Riptide UDP. Large saves use the adjacent TCP port, with chunked UDP transfer as a fallback.
- The handshake compares the game build, protocol version, packet registry, mod version, main DLL SHA-256, DLC selection, and enabled-mod fingerprint.
- The dedicated-server prototype remains in the repository but is not included in the Workshop release.

## Installation

See [INSTALL.md](INSTALL.md) for Steam Workshop installation, source builds, LAN ports, and release packaging.

Do not enable a Workshop copy and a local source build at the same time. Every player must use the same game build, DLC selection, enabled mods, load order, configuration, and ONI Together DLL.

## Usage

1. Enable `Oxygen Not Included Together` for the active DLC on the Mods screen and restart the game.
2. Open Multiplayer from the main menu.
3. For Steam play, the host creates a lobby and the other players join it.
4. For LAN play, the host listens on UDP `8080` by default. Save transfer uses TCP `8081`.

## Synchronization model

The host owns authoritative colony state. A joining client receives a full snapshot tied to the current session generation. Reliable changes produced during loading are retained until the client confirms that the snapshot is loaded.

Runtime validation splits state into five domains: `grid`, `entity`, `storage`, `world`, and `rocket`. Each validation segment records the raw hashes, applies a host keyframe, waits for its acknowledgement, and compares the post-keyframe hashes.

Entity lifecycle updates use monotonic revisions and tombstones. Failed identity claims restore the previous NetId, lifecycle journal, position, and active state instead of leaving a partial binding.

## Validation record

On July 18, 2026, both test machines completed 539 in-game Debug checks: 513 passed, none failed, and 26 were skipped because the required runtime state was not present.

The native two-machine soak ran for 21 segments and 37,800 ticks with ONI MCP Server disabled. All five post-keyframe domain hashes matched in every segment. The final record reported `postMismatchSeen=False`, `keyframeApplyFailureSeen=False`, and `postKeyframeEqual=True`; lifecycle missing, unexpected, tombstoned-live, and unassigned counts were all zero.

Raw drift before a keyframe is a diagnostic signal. A post-keyframe mismatch is a release failure.

## Development

Debug builds expose three in-game entry points:

- `Shift+F2` opens the test menu.
- `Shift+F3` discovers and runs all in-game unit tests.
- `Shift+F4` runs the Riptide loopback smoke test on `127.0.0.1:27777`.

The project targets `netstandard2.1`. One DLL is used on macOS, Windows, and Linux, while the release directory includes the UI asset bundle for each platform.

## License

ONI Together is licensed under the [MIT License](LICENSE.md). Original project credit and third-party notices are preserved in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
