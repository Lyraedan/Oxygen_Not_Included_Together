# Security

## Trust boundaries
- Only connected clients with handshake key `ONI_TOGETHER` + lobby cap accepted. Current relay model still trusts **master** client for simulation; not authoritative. Master migration does **not** transfer sim state.
- User-controlled paths are sandboxed. `SaveStore` writes only inside saves dir; atomic temp+move; backups. `ResolveSaveFilePath` confines; external absolute save reads require `ONI_ALLOW_EXTERNAL_SAVE_PATHS=true`. `PathSandbox` rejects `..`, rooted escapes, control chars, unsafe filenames.
- Config saved atomically. `Sanitize()` clamps ports/tick/rate/size, caps string lengths, strips control chars, de-dupes/cleans DLC ids, removes required∩forbidden.
- Save loading caps `MaxSaveBytes`; no execution of save content.

## Network hardening
- Handshake key + max lobby. Optional `ONI_DISCOVERY_TOKEN`. `ONI_DISCOVERY_ENABLED`.
- Packet size caps `ONI_MAX_PACKET_SIZE` / `MaxMessageSize`. Connection payload cap. Per-peer `RateLimiter`. `ONI_MAX_MESSAGES_PER_POLL` + `ONI_RATE_LIMIT_MESSAGES_PER_SECOND`. `MaxMessagesPerPoll` enforced.
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
