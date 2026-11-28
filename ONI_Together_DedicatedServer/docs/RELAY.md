# How the relay works

The server does **not** simulate the colony. One player's game does: the **master**. The server forwards packets and keeps the colony's save.

## Roles

| Peer | What it sees | Mod behaviour |
|---|---|---|
| First to connect (master) | Framed messages (`RelayProtocol`) | Hosts through `RelayHostTransport` |
| Everyone after | Ordinary mod packets | Unchanged: the relay looks like a direct LAN host with id 1 |

Players get the id `peer.Id + 2`, the same a direct host assigns and the same the mod derives from `RemoteId + 2`. Only the master link is framed (`Shared/Networking/Dedicated/RelayProtocol.cs`, compiled into both sides). The framing is not an `IPacket`, so it does not change the packet registry fingerprint.

The connect request is the key `ONI_TOGETHER` followed by an instance token, the relay protocol version and the active and owned DLC ids. A direct LAN host reads only the key.

## Session flow

1. The master connects. The relay tells it whether it holds a colony.
2. The master downloads that colony, loads it (which drops the connection) and reconnects. The relay keeps the slot for that game instance for 10 minutes. Until it hosts, the master has no active client session: duplicants, critters and buildings set themselves up as client or host once, when a session becomes active, so a client session here would leave the whole world in client mode. A world that was not loaded from the server's colony (for example a client's world after a reconnect) is never hosted; the master loads the server's colony instead.
3. The master sends `HostReady`. Other players are refused until then.
4. Players join with the normal flow: state request, save from the master's live world, load, reconnect.
5. Every game save by the master (autosave, manual, or the one made for a joining player) is uploaded and replaces the server's `current.sav`.
6. When the master leaves, everyone is disconnected. The next player to connect hosts, starting from the stored colony.

There is no live host migration: a client's world is not an authoritative one. **Progress since the master's last save is not on the server.** If the server connection drops while hosting, the mod saves the colony on the master's machine.

The master stores the downloaded colony as `<name> (Dedicated)` so it does not overwrite that player's own save of the same colony.

## First start

The server needs a colony. Put a `.sav` into the server's saves directory and start once with `--save "My Colony.sav"`. From then on the stored colony wins over `--save`, also after a restart.

## Limits that disconnect

A reliable packet is never dropped silently, because the game would desync. A player who sends an oversized packet or floods reliable packets (more than ten seconds' worth of `RateLimitMessagesPerSecond` at once) is disconnected. Only unreliable packets are dropped by the rate limit. The master is not rate limited.

## Known limitations

- Not yet tested with real game instances.
- Loading another save while hosting through the relay ends the relay session; the mod's existing "host again after load" step then hosts directly, not through the relay.
- No TCP save transfer behind the relay; joining uses the chunked UDP transfer.
- Refused connections (host not ready, DLC, old mod) show the mod's generic "connection rejected" message; the reason is in the server log.
- The master needs the LiteNetLib LAN transport.

## Testing with the real game

1. Build the mod and start the server with `--save` as above.
2. Game 1: join by LAN with the server's address and port. It should download the colony, load it, and report "server started".
3. Game 2: join the same address. Check that dig and build orders from both sides show up.
4. Save in game 1; the server log should report the received colony.
5. Quit game 1; game 2 should be disconnected. Reconnect game 2: it should now host from the saved state.
