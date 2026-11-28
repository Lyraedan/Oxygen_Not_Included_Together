# ONI Together API

API for third-party mods to add multiplayer features via **Oxygen Not Included Together**.

## Installation

```
dotnet add package ONI_Together_API
```

## Usage

### Check if ONI Together is installed and enabled

```csharp
using ONI_Together_API;

if (MP_Mod_Info.MultiplayerModPresent)
{
    // ONI Together is detected and enabled
}
```

### Access session info

```csharp
if (SessionInfoAPI.InSession)
{
    bool isHost = SessionInfoAPI.IsHost;
    bool isClient = SessionInfoAPI.IsClient;
    ulong localUserId = SessionInfoAPI.LocalUserID();
    ulong hostUserId = SessionInfoAPI.HostUserID();
}
```

### Creating custom packets
```csharp
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together_API.Networking;

public class ExamplePacket : IPacket
{
    private ulong SenderId;
    private string Message;

    public ExamplePacket(ulong senderId, string message) 
    {
        SenderId = senderId;
        Message = message;
    }
    
    public void Serialize(BinaryWriter writer) 
    {
        writer.Write(SenderId);
        writer.Write(Message);
    }
    
    public void Deserialize(BinaryReader reader) 
    {
        SenderId = reader.ReadUInt64();
        Message = reader.ReadString();
    }
    
    public void OnDispatched() 
    {
        Console.WriteLine($"Received example packet from {SenderId}: {Message}");
        if (SessionInfoAPI.IsHost) return;
        
        Console.WriteLine($"Recieved this message from the host: {Message}");
    }
}
```

### Sending custom packets
```csharp
public void SendMyCoolPacket() 
{
    if (SessionInfoAPI.IsClient) return;
    
    // Only the host can send this
    ExamplePacket packet = new ExamplePacket(SessionInfoAPI.LocalUserID(), "Hello ONI Together");
    PacketSenderAPI.SendToAllClients(packet, PacketSendMode.Reliable);
}
```

### Spawning networked entities

Only the host can spawn entities. Call `SpawnUtilsAPI.KNetInstantiate` and the spawn is replicated to all clients automatically. Both methods return `null` when ONI Together is not loaded.

```csharp
using ONI_Together_API.Misc;
using UnityEngine;

// Spawn a prefab by GameObject
GameObject spawned = SpawnUtilsAPI.KNetInstantiate(somePrefab, position);

// Spawn an element resource (e.g. ore) with mass, temperature and disease data preserved
GameObject ore = SpawnUtilsAPI.KNetInstantiate((int)SimHashes.IronOre, position, 100f, 293.15f, 0, 0);
```

### Looking up networked entities

```csharp
using ONI_Together_API.Networking;
using UnityEngine;

if (NetworkIdentityRegistryAPI.TryGet(netId, out GameObject entity))
{
    // ...
}

if (NetworkIdentityRegistryAPI.TryGetComponent<PrimaryElement>(netId, out var primaryElement))
{
    // ...
}
```

## Custom network transports

Third-party mods can register their own transport. ONI Together keeps packet serialization,
framing, chunking, batching and dispatch; a custom transport only moves raw bytes and tracks
peers.

Register in `OnAllModsLoaded`:

```csharp
using ONI_Together_API.Transport;

public sealed class MyTransportProvider : TransportProviderBase
{
    public override string Id => "my-transport";
    public override string DisplayName => "My Transport";
    public override TransportCapabilities Capabilities => TransportCapabilities.DirectConnect;

    public override TransportSetting[] GetSettings() => new[]
    {
        new TransportSetting
        {
            Key = "address",
            Label = "Address",
            Kind = TransportSettingKind.Text,
            Scope = TransportSettingScope.Both,
            DefaultValue = "127.0.0.1",
            Required = true
        },
        new TransportSetting
        {
            Key = "port",
            Label = "Port",
            Kind = TransportSettingKind.Integer,
            Scope = TransportSettingScope.Both,
            DefaultValue = "8080",
            Required = true
        }
    };

    public override ITransportServer CreateServer() => new MyServer();
    public override ITransportClient CreateClient() => new MyClient();
}

// after all mods have loaded
TransportAPI.RegisterTransport(new MyTransportProvider());
```

The server and client implementations must use public implicit members. ONI Together binds
them by reflection. Key rules:

- `TryDequeueReceived(out ulong senderId, out byte[] payload)` is polled from `Update()` on the
  Unity main thread. Queue packets from background network threads and only drain them here.
- `Send(ulong playerId, byte[] payload, int sendMode)` receives an already-framed payload.
  `sendMode` uses the ONI Together packet send flags. Do not add packet headers.
- Player ids must be stable, non-zero `ulong` values. The server's `LocalPlayerId` is the host,
  and `GetConnectedPlayerIds()` must include the host and every peer that can send packets.
  Add peers as soon as they connect so the handshake packet can mark them protocol-verified.
- `Start(Dictionary<string, string> settings)` and `Connect(...)` receive the schema values plus
  these standard keys: `transportId`, `displayName`, `maxPlayers` (host only), `isHost`.
- The server owns the native transport lifetime; `Stop()` must release it.
- `CreateServer()` and `CreateClient()` must both return non-null objects. The unused side may be
  a lightweight stub; the main mod creates both when selecting a custom transport.
- `ApiVersion` must be `TransportAPI.CurrentApiVersion` (`1`) for the current contract.
- `GetSettings()` drives the Custom Transport tab. No address or port fields are assumed.
  Boolean settings are passed as `"1"` or `"0"`; integer settings are passed as decimal strings.
  A setting's `DefaultValue` is passed when the user has not overridden it.

The Custom Transport tab and the transport API are additive; built-in Steam, LiteNetLib and
Riptide transports are unchanged.

## Requirements

- Oxygen Not Included (with ONI Together mod installed)
- .NET Standard 2.1 compatible project

## Links

- [GitHub](https://github.com/Lyraedan/Oxygen_Not_Included_Together)
- [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3630759126)
- [Discord](https://discord.gg/jpxveK6mmY)
- [Trello Board](https://trello.com/b/kq7yVWyU/oxygen-not-included-together)
- [Ko-fi](https://ko-fi.com/lyraedan)

## License

MIT
