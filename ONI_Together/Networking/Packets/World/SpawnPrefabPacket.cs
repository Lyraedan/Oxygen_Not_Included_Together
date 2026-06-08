using System.IO;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using UnityEngine;

namespace ONI_Together.Networking.Packets.World;

public class SpawnPrefabPacket : IPacket
{
    public int NetId;
    public int Hash;
    public Vector3 Position;

    public bool HasElementData = false;
    public float Mass;
    public float Temperature;
    public byte DiseaseIndex;
    public int DiseaseCount;

    public SpawnPrefabPacket(int netId, int hash, Vector3 position)
    {
        NetId = netId;
        Hash = hash;
        Position = position;
        HasElementData = false;
    }
    
    public SpawnPrefabPacket(int netId, int hash, Vector3 position, float mass, float temperature, byte diseaseIndex, int diseaseCount)
    {
        NetId = netId;
        Hash = hash;
        Position = position;
        HasElementData = true;
        Mass = mass;
        Temperature = temperature;
        DiseaseIndex = diseaseIndex;
        DiseaseCount = diseaseCount;
    } 
    
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(NetId);
        writer.Write(Hash);
        writer.Write(Position);
        writer.Write(HasElementData);
        if (!HasElementData) return;
        
        writer.Write(Mass);
        writer.Write(Temperature);
        writer.Write(DiseaseIndex);
        writer.Write(DiseaseCount);
    }

    public void Deserialize(BinaryReader reader)
    {
        NetId = reader.ReadInt32();
        Hash = reader.ReadInt32();
        Position = reader.ReadVector3();
        HasElementData = reader.ReadBoolean();
        if (!HasElementData) return;
        
        Mass = reader.ReadSingle();
        Temperature = reader.ReadSingle();
        DiseaseIndex =  reader.ReadByte();
        DiseaseCount = reader.ReadInt32();
    }

    public void OnDispatched()
    {
        if (MultiplayerSession.IsHost) return;

        GameObject go;
        if (HasElementData)
        {
            var element = ElementLoader.GetElement(new Tag(Hash));
            if (element == null) return;
            go = element.substance.SpawnResource(Position, Mass, Temperature, DiseaseIndex, DiseaseCount);
        }
        else
        {
            var prefab = Assets.GetPrefab(new Tag(Hash));
            if (prefab == null) return;
            go = Util.KInstantiate(prefab, Position);
            go.SetActive(true);
        }
        
        go.AddOrGet<NetworkIdentity>().OverrideNetId(NetId);
        
        // Race condition guard: Was this prefab already picked up / stored before the packet arrived?
        if (GroundItemPickedUpPacket.TryConsumePending(NetId) || StorageItemPacket.TryConsumePending(NetId))
        {
            Util.KDestroyGameObject(go);
        }
    }
}