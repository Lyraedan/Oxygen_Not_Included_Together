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
    public static GameObject KNetInstantiate(GameObject prefab, Vector3 position, bool isActive = true)
    {
        if (!MultiplayerSession.IsHost) return null;
        
        var go = Util.KInstantiate(prefab, position);
        go.SetActive(isActive);
        var identity = AssignIdentity(go);

        SpawnPrefabPacket packet = new SpawnPrefabPacket(identity.NetId, go.PrefabID().GetHashCode(), position);
        packet.IsActive = isActive;
        PacketSender.SendToAllClients(packet);
        return go;
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
    public static GameObject KNetInstantiate(int elementHash, Vector3 position, float mass, float temperature, byte diseaseIdx, int diseaseCount)
    {
        if (!MultiplayerSession.IsHost) return null;

        Element element = ElementLoader.GetElement(new Tag(elementHash));
        if (element == null) return null;
        
        var go = element.substance.SpawnResource(position, mass, temperature, diseaseIdx, diseaseCount);
        var identity = AssignIdentity(go);

        SpawnPrefabPacket packet = new SpawnPrefabPacket(identity.NetId, elementHash, position, mass, temperature, diseaseIdx, diseaseCount);
        PacketSender.SendToAllClients(packet);
        return go;
    }
}