using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using UnityEngine;

namespace ONI_Together.Misc;

public static class SpawnUtils
{
    private static NetworkIdentity AssignIdentity(GameObject go)
    {
        var identity = go.AddOrGet<NetworkIdentity>();
        if (identity.NetId == 0)
            identity.RegisterIdentity();
        return identity;
    }
    
    /// <summary>
    /// Spawns a prefab by <see cref="GameObject"/> on the host, assigns it a <see cref="NetworkIdentity"/>,
    /// and broadcasts a <see cref="SpawnPrefabPacket"/> to all clients so they replicate the spawn.
    /// Uses <c>Util.KInstantiate</c> under the hood with network sync built in.
    /// </summary>
    /// <param name="tag">The prefab tag to spawn.</param>
    /// <param name="position">World position for the new GameObject.</param>
    /// <returns>The spawned GameObject on the host, or <c>null</c> if not host or prefab not found.</returns>
    [API_Method]
    public static GameObject KNetInstantiate(GameObject prefab, Vector3 position, bool isActive = true)
    {
        if (!MultiplayerSession.IsHost) return null;
        
        var go = Util.KInstantiate(prefab, position);
        go.SetActive(isActive);
        BroadcastSpawn(go, isActive);
        return go;
    }

    /// <summary>
    /// Gives an object the host has already spawned a <see cref="NetworkIdentity"/> and broadcasts a
    /// <see cref="SpawnPrefabPacket"/> so the clients spawn it too. For objects the game creates
    /// itself, e.g. <c>Scenario.SpawnPrefab</c> (see ScenarioSpawnPrefabPatch).
    /// </summary>
    /// <param name="go">The GameObject the host has spawned.</param>
    /// <param name="isActive">Whether the clients should spawn it active.</param>
    /// <returns>The object's NetId, or 0 if it could not be registered.</returns>
    [API_Method]
    public static int BroadcastSpawn(GameObject go, bool isActive = true)
    {
        if (!MultiplayerSession.IsHost) return 0;
        
        if (go == null)
            return 0;

        var identity = AssignIdentity(go);
        if (identity.NetId == 0)
            return 0;

        SpawnPrefabPacket packet = new SpawnPrefabPacket(identity.NetId, go.PrefabID().GetHashCode(), go.transform.position);
        packet.IsActive = isActive;
        PacketSender.SendToAllClients(packet);
        return identity.NetId;
    }

    /// <summary>
    /// Spawns an element resource (e.g. ore from digging) on the host, assigns it a
    /// <see cref="NetworkIdentity"/>, and broadcasts a <see cref="SpawnPrefabPacket"/> to all clients.
    /// Uses <c>element.substance.SpawnResource</c> under the hood so temperature, mass, and disease
    /// data are preserved identically on both sides.
    /// </summary>
    /// <param name="elementHash">The <see cref="SimHashes"/> value of the element (cast to <c>int</c>). Use <c>(int)element.id</c>.</param>
    /// <param name="position">World position for the new resource.</param>
    /// <param name="mass">Mass of the resource in kg.</param>
    /// <param name="temperature">Temperature of the resource.</param>
    /// <param name="diseaseIdx">Disease index (0 = no disease).</param>
    /// <param name="diseaseCount">Disease germ count.</param>
    /// <returns>The spawned GameObject on the host, or <c>null</c> if not host or element not found.</returns>
    [API_Method]
    public static GameObject KNetInstantiate(int elementHash, Vector3 position, float mass, float temperature, byte diseaseIdx, int diseaseCount)
    {
        if (!MultiplayerSession.IsHost) return null;

        Element element = ElementLoader.GetElement(new Tag(elementHash));
        if (element == null) return null;
        
        var go = element.substance.SpawnResource(position, mass, temperature, diseaseIdx, diseaseCount);
        BroadcastResourceSpawn(go);
        return go;
    }

    /// <summary>
    /// The resource version of <see cref="BroadcastSpawn"/>: gives an element resource the host has already
    /// spawned (e.g. with <c>Substance.SpawnResource</c>) a <see cref="NetworkIdentity"/> and broadcasts a
    /// <see cref="SpawnPrefabPacket"/> with the element, mass, temperature and disease of its
    /// <see cref="PrimaryElement"/>, so the clients spawn the same resource. Anything that is not an element
    /// resource is sent as a prefab spawn (<see cref="BroadcastSpawn"/>).
    /// </summary>
    /// <param name="go">The resource GameObject the host has spawned.</param>
    /// <returns>The object's NetId, or 0 if it could not be registered.</returns>
    [API_Method]
    public static int BroadcastResourceSpawn(GameObject go)
    {
        if (!MultiplayerSession.IsHost) return 0;
        
        if (go == null)
            return 0;

        var primaryElement = go.GetComponent<PrimaryElement>();
        if (primaryElement == null || primaryElement.Element == null || go.PrefabID() != primaryElement.Element.tag)
            return BroadcastSpawn(go, go.activeSelf); // Still not 100% sure about this fallback, but I'll leave it here

        var identity = AssignIdentity(go);
        if (identity.NetId == 0)
            return 0;

        SpawnPrefabPacket packet = new SpawnPrefabPacket(identity.NetId, (int)primaryElement.ElementID, go.transform.position,
            primaryElement.Mass, primaryElement.Temperature, primaryElement.DiseaseIdx, primaryElement.DiseaseCount);
        PacketSender.SendToAllClients(packet);
        return identity.NetId;
    }
}