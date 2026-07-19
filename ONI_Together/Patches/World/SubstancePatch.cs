using HarmonyLib;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World
{
	[HarmonyPatch(typeof(EntityTemplates), nameof(EntityTemplates.CreateOreEntity))]
	public static class EntityTemplatesCreateOreEntityPatch
	{
		public static void Postfix(ref GameObject __result)
		{
			if (__result != null)
				__result.AddOrGet<NetworkIdentity>();
		}
	}

	[HarmonyPatch(typeof(Substance), nameof(Substance.SpawnResource))]
	public static class Substance_SpawnResource_Patch
	{
		public static void Postfix(ref GameObject __result)
		{
			using var _ = Profiler.Scope();

			if (__result == null)
				return;
			if (SpawnPrefabPacket.TryConsumeClientReplay(
				    __result, out GameObject authoritative))
			{
				__result = authoritative;
				return;
			}

			NetworkIdentity identity = __result.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();
			identity.EnsureAuthoritativeSpawnBroadcast();
		}
	}

}
