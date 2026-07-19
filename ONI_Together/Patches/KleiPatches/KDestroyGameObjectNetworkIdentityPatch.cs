using HarmonyLib;
using ONI_Together.Networking.Components;
using UnityEngine;

namespace ONI_Together.Patches.KleiPatches
{
	[HarmonyPatch(typeof(Util), nameof(Util.KDestroyGameObject), typeof(GameObject))]
	public static class KDestroyGameObjectNetworkIdentityPatch
	{
		public static void Prefix(GameObject __0)
		{
			if (__0 == null || __0.IsNullOrDestroyed())
				return;
			foreach (NetworkIdentity identity in
			         __0.GetComponentsInChildren<NetworkIdentity>(includeInactive: true))
				identity.MarkDestructionPending();
		}
	}
}
