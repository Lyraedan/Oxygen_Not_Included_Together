![Logo](https://i.ibb.co/BVFcTP0S/onit.png)

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
