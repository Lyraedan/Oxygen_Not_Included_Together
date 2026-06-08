using System.IO;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using UnityEngine;

namespace ONI_Together.Networking.Packets.World;

public class SpawnPrefabPacket : IPacket
{
    public int NetId;
    public string PrefabHash;
    public Vector3 Position;

    public bool HasElementData = false;
    public ushort ElementIndex;
    public float Mass;
    public float Temperature;
    public byte DiseaseIndex;
    public int DiseaseCount;

    public SpawnPrefabPacket(int netId, string prefabHash, Vector3 position)
    {
        NetId = netId;
        PrefabHash = prefabHash;
        Position = position;
        HasElementData = false;
    }
    
    public SpawnPrefabPacket(int netId, string prefabHash, Vector3 position, ushort elementIndex, float mass, float temperature, byte diseaseIndex, int diseaseCount)
    {
        NetId = netId;
        PrefabHash = prefabHash;
        Position = position;
        HasElementData = true;
        ElementIndex = elementIndex;
        Mass = mass;
        Temperature = temperature;
        DiseaseIndex = diseaseIndex;
        DiseaseCount = diseaseCount;
    } 
    
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(NetId);
        writer.Write(PrefabHash);
        writer.Write(Position);
        writer.Write(HasElementData);
        if (!HasElementData) return;
        
        writer.Write(ElementIndex);
        writer.Write(Mass);
        writer.Write(Temperature);
        writer.Write(DiseaseIndex);
        writer.Write(DiseaseCount);
    }

    public void Deserialize(BinaryReader reader)
    {
        NetId = reader.ReadInt32();
        PrefabHash = reader.ReadString();
        Position = reader.ReadVector3();
        HasElementData = reader.ReadBoolean();
        if (!HasElementData) return;
        
        ElementIndex = reader.ReadUInt16();
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
            var element = ElementLoader.GetElement(new Tag(ElementIndex));
            if (element == null) return;
            go = element.substance.SpawnResource(Position, Mass, Temperature, DiseaseIndex, DiseaseCount);
        }
        else
        {
            var prefab = Assets.GetPrefab(TagManager.Create(PrefabHash));
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