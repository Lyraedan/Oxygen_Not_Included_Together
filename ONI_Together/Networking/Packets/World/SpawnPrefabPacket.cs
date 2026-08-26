using System;
using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.OxySync.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Scripts.Creatures;
using ONI_Together.Scripts.Duplicants;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.World;

public class SpawnPrefabPacket : IPacket
{
    public static bool ProcessingIncoming;

    public int NetId;
    public int Hash;
    public string PrefabName = string.Empty;
    public Vector3 Position;
    public bool IsActive = true;

    public bool HasElementData = false;
    public float Mass;
    public float Temperature;
    public byte DiseaseIndex;
    public int DiseaseCount;

    public SpawnPrefabPacket() { }

    public SpawnPrefabPacket(int netId, int hash, Vector3 position, string prefabName = null)
    {
        NetId = netId;
        Hash = hash;
        PrefabName = prefabName ?? string.Empty;
        Position = position;
        HasElementData = false;
    }
    
    public SpawnPrefabPacket(int netId, int hash, Vector3 position, float mass, float temperature, byte diseaseIndex, int diseaseCount, string prefabName = null)
    {
        NetId = netId;
        Hash = hash;
        PrefabName = prefabName ?? string.Empty;
        Position = position;
        HasElementData = true;
        Mass = mass;
        Temperature = temperature;
        DiseaseIndex = diseaseIndex;
        DiseaseCount = diseaseCount;
    }
    
    public void Serialize(BinaryWriter writer)
    {
        using var _ = Profiler.Scope();

        writer.Write(NetId);
        writer.Write(Hash);
        writer.Write(PrefabName ?? string.Empty);
        writer.Write(Position);
        writer.Write(IsActive);
        writer.Write(HasElementData);
        if (!HasElementData) return;
        
        writer.Write(Mass);
        writer.Write(Temperature);
        writer.Write(DiseaseIndex);
        writer.Write(DiseaseCount);
    }

    public void Deserialize(BinaryReader reader)
    {
        using var _ = Profiler.Scope();

        NetId = reader.ReadInt32();
        Hash = reader.ReadInt32();
        PrefabName = reader.ReadString();
        Position = reader.ReadVector3();
        IsActive = reader.ReadBoolean();
        HasElementData = reader.ReadBoolean();
        if (!HasElementData) return;
        
        Mass = reader.ReadSingle();
        Temperature = reader.ReadSingle();
        DiseaseIndex = reader.ReadByte();
        DiseaseCount = reader.ReadInt32();
    }

    public void OnDispatched()
    {
        using var _ = Profiler.Scope();

        if (MultiplayerSession.IsHost) return;

        ProcessingIncoming = true;
        try
        {
            // If already registered with this NetId on client, do not spawn a duplicate
            if (NetId != 0 && NetworkIdentityRegistry.TryGet(NetId, out var existing) && existing != null)
            {
                return;
            }

            GameObject go = null;
            if (HasElementData)
            {
                Element element = null;
                if (!string.IsNullOrEmpty(PrefabName))
                    element = ElementLoader.FindElementByName(PrefabName);
                if (element == null)
                    element = ElementLoader.GetElement(new Tag(Hash));

                if (element != null && element.substance != null)
                {
                    go = element.substance.SpawnResource(Position, Mass, Temperature, DiseaseIndex, DiseaseCount);
                }
            }
            
            if (go == null)
            {
                GameObject prefab = null;
                string lookupName = PrefabName;
                if (!string.IsNullOrEmpty(lookupName) && lookupName.Contains("|"))
                    lookupName = lookupName.Split('|')[0];

                if (!string.IsNullOrEmpty(lookupName))
                    prefab = Assets.GetPrefab(new Tag(lookupName)) ?? Assets.GetPrefab(lookupName);
                if (prefab == null)
                    prefab = Assets.GetPrefab(new Tag(Hash));

                if (prefab == null)
                {
                    DebugConsole.LogWarning($"[SpawnPrefabPacket] Prefab not found (Name: '{PrefabName}', Hash: {Hash})");
                    return;
                }

                // If this is a building prefab, it must be constructed via BuildingDef.Build to properly initialize PrimaryElement
                if (prefab.TryGetComponent<Building>(out var buildingComp) || prefab.GetComponent<BuildingComplete>() != null)
                {
                    BuildingDef def = buildingComp != null ? buildingComp.Def : Assets.GetBuildingDef(prefab.name.Replace("Complete", ""));
                    if (def != null)
                    {
                        int cell = Grid.PosToCell(Position);
                        if (Grid.IsValidCell(cell))
                        {
                            go = def.Build(cell, Orientation.Neutral, null, def.DefaultElements(), Temperature > 0 ? Temperature : 293.15f, playsound: false, GameClock.Instance.GetTime());
                        }
                    }
                }

                if (go == null)
                {
                    // If this is a duplicant prefab, instantiate using MinionStartingStats.Deliver
                    if (prefab.GetComponent<MinionIdentity>() != null || prefab.HasTag(GameTags.BaseMinion) || (PrefabName != null && PrefabName.StartsWith("Minion", StringComparison.OrdinalIgnoreCase)))
                    {
                        string personalityId = null;
                        string dupeName = null;
                        int voiceIdx = -1;

                        if (!string.IsNullOrEmpty(PrefabName) && PrefabName.Contains("|"))
                        {
                            var parts = PrefabName.Split('|');
                            if (parts.Length > 1) personalityId = parts[1];
                            if (parts.Length > 2) dupeName = parts[2];
                            if (parts.Length > 3 && int.TryParse(parts[3], out int v)) voiceIdx = v;
                        }
                        else if (!string.IsNullOrEmpty(PrefabName) && !string.Equals(PrefabName, "Minion", StringComparison.OrdinalIgnoreCase))
                        {
                            personalityId = PrefabName;
                        }

                        Personality personality = null;
                        if (!string.IsNullOrEmpty(personalityId))
                        {
                            personality = Db.Get()?.Personalities?.TryGet(personalityId);
                            if (personality == null)
                                personality = Db.Get()?.Personalities?.TryGet(new HashedString(personalityId));
                        }
                        if (personality == null && Db.Get()?.Personalities?.resources != null && Db.Get().Personalities.resources.Count > 0)
                        {
                            personality = Db.Get().Personalities.resources[0];
                        }

                        if (personality != null)
                        {
                            var stats = new MinionStartingStats(personality);
                            if (!string.IsNullOrEmpty(dupeName))
                                stats.Name = dupeName;
                            if (voiceIdx >= 0)
                                stats.voiceIdx = voiceIdx;

                            go = stats.Deliver(Position);
                        }
                        if (go == null)
                        {
                            go = Util.KInstantiate(prefab, Position);
                            go.SetActive(IsActive);
                        }

                        var netIdComp = go.AddOrGet<NetworkIdentity>();
                        netIdComp.NetId = NetId;
                        netIdComp.OverrideNetId(NetId);
                    }
                    else
                    {
                        go = Util.KInstantiate(prefab, Position);
                        var netIdComp = go.AddOrGet<NetworkIdentity>();
                        netIdComp.NetId = NetId;
                        go.SetActive(IsActive);
                        netIdComp.OverrideNetId(NetId);
                    }
                }
            }
            
            if (go != null)
            {
                var identity = go.AddOrGet<NetworkIdentity>();
                if (identity.NetId != NetId)
                    identity.OverrideNetId(NetId);

                if (go.GetComponent<MinionIdentity>() != null || go.HasTag(GameTags.BaseMinion))
                {
                    go.AddOrGet<OxySyncEntityPositionHandler>();
                    go.AddOrGet<AnimStateSyncer>();
                    go.AddOrGet<MinionMultiplayerInitializer>();
                }
                else if (go.GetComponent<CreatureBrain>() != null || go.HasTag(GameTags.Creature))
                {
                    go.AddOrGet<OxySyncEntityPositionHandler>();
                    go.AddOrGet<AnimStateSyncer>();
                    go.AddOrGet<CreatureMultiplayerInitializer>();
                }

                DebugConsole.Log($"[SpawnPrefabPacket] Spawned entity '{go.name}' (NetId: {NetId}) at {Position}");
                
                // Race condition guard: ONLY for loose substance/ore ground resources, NEVER destroy living creatures / plants / minions / buildings!
                if (HasElementData || go.GetComponent<SubstanceChunk>() != null)
                {
                    if (GroundItemPickedUpPacket.TryConsumePending(NetId) || StorageItemPacket.TryConsumePending(NetId))
                    {
                        DebugConsole.Log($"[SpawnPrefabPacket] Consumed pending pickup for resource NetId {NetId}");
                        Util.KDestroyGameObject(go);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugConsole.LogError($"[SpawnPrefabPacket] Exception spawning prefab (Name='{PrefabName}', Hash={Hash}, NetId={NetId}): {ex}");
        }
        finally
        {
            ProcessingIncoming = false;
        }
    }
}