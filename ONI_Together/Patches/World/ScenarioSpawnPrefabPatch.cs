using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World
{
	[HarmonyPatch(typeof(Scenario), nameof(Scenario.SpawnPrefab), [typeof(int), typeof(int), typeof(int), typeof(string), typeof(Grid.SceneLayer)])]
	public static class ScenarioSpawnPrefabPatch
	{
		public static void Postfix(GameObject __result)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHostInSession || __result == null)
				return;

			var identity = __result.AddOrGet<NetworkIdentity>();
			if (identity.NetId == 0)
				identity.RegisterIdentity();

			if (identity.NetId == 0)
				return;

			var tag = __result.PrefabID();
			Vector3 pos = __result.transform.position;

			var packet = new SpawnPrefabPacket(identity.NetId, tag.GetHashCode(), pos)
			{
				IsActive = __result.activeSelf
			};

			PacketSender.SendToAllClients(packet);
		}
	}
}
