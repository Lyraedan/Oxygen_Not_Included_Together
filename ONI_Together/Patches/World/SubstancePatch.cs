using HarmonyLib;
using ONI_Together.Networking.Components;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World
{
	[HarmonyPatch(typeof(Substance), nameof(Substance.SpawnResource))]
	public static class Substance_SpawnResource_Patch
	{
		public static void Postfix(GameObject __result)
		{
			using var _ = Profiler.Scope();

			if (__result == null)
				return;

			NetworkIdentity identity = __result.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();
		}
	}

}
