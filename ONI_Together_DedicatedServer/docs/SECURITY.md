# Security

## Trust boundaries
- Only clients with handshake key `ONI_TOGETHER`, a matching relay hello and a free lobby slot are accepted. The server trusts the **master** (the first to connect) with the simulation and the stored colony: **whoever connects first while no master is present can replace the stored save** (backups are kept). There is no live host migration.
- User-controlled paths are sandboxed. `SaveStore` writes only inside saves dir; atomic temp+move; backups. `ResolveSaveFilePath` confines; external absolute save reads require `ONI_ALLOW_EXTERNAL_SAVE_PATHS=true`. `PathSandbox` rejects `..`, rooted escapes, control chars, unsafe filenames.
- Config saved atomically. `Sanitize()` clamps ports/tick/rate/size, caps string lengths, strips control chars, de-dupes/cleans DLC ids, removes required∩forbidden.
- Save loading caps `MaxSaveBytes`; no execution of save content.

## Network hardening
- Handshake key + max lobby. Optional `ONI_DISCOVERY_TOKEN`. `ONI_DISCOVERY_ENABLED`.
- Packet size caps `ONI_MAX_PACKET_SIZE` / `MaxMessageSize` and a connection payload cap. Per-peer rate limit (`ONI_RATE_LIMIT_MESSAGES_PER_SECOND`): excess unreliable packets are dropped; a reliable flood or an oversized packet disconnects the peer, because silently dropping reliable game traffic would desync. `MaxMessagesPerPoll` is no longer applied.
- Malformed packets caught; no crash. Console line length capped; CLI arg length capped. No command injection (no shell invocation). No secrets logged. `VerbosePacketLogging` logs type/size not payload.
- SIGINT/SIGTERM graceful; `--non-interactive` for containers. `reloadconfig` re-reads live config without restarting transport or dropping players; `Ip`/`Port`/`Transport` require manual restart; not a security boundary.

## DLC ownership (Klei requirement)
- `DlcCheckMode` Off/Declared/Required; `ONI_REQUIRED_DLC_IDS`, `ONI_FORBIDDEN_DLC_IDS`, `ONI_SERVER_ACTIVE_DLC_IDS`, `ONI_REQUIRE_DLC_DECLARATION`, `ONI_DLC_CHECK_MODE`.
- `DeclaredDlcOwnershipVerifier` enforces **client-declared** owned/active DLC + server match. **This cannot cryptographically verify Klei/Steam ownership.** Combine with existing ONI client-side ownership + `SaveHelper.SavegameDlcListValid` + `GameStateRequestPacket.ActiveDlcIds`. True ownership needs Klei/Steam integration (blocked).
- Existing client-side DLC-mismatch checks help but server must still enforce.

## Container
Non-root `app`, no root, no package manager runtime, `/data` volume, `--health-check`. No secrets in image.

## Not a full security audit
No auth of individual clients beyond key + optional DLC; no TLS (LiteNetLib relay; not introducing without protocol support); no protection against a malicious master (master runs sim). Do not expose to untrusted networks without firewall.
