using HarmonyLib;
using ONI_MP.DebugTools;
using ONI_MP.Networking;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.World;
using Shared.Profiling;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.World
{
	[HarmonyPatch(typeof(Pickupable), "OnCleanUp")]
	public static class PickupableCleanedUpPatch
	{
		private static long _skipCount;

		public static void Postfix(Pickupable __instance)
		{
			using var _ = Profiler.Scope();
			try
			{
				if (!MultiplayerSession.IsHost || !MultiplayerSession.InSession)
					return;

				var identity = __instance.GetComponent<NetworkIdentity>();
				if (identity == null || identity.NetId == 0)
				{
					long n = ++_skipCount;
					if (n <= 5 || n % 100 == 0)
					{
						string name = __instance != null && __instance.gameObject != null ? __instance.gameObject.name : "<null>";
						DebugConsole.Log($"[GroundPickup] skip NetId=0 name={name} #{n}");
					}
					return;
				}

				PacketSender.SendToAllClients(new GroundItemPickedUpPacket { NetId = identity.NetId });
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[PickupableCleanedUpPatch] Exception: {ex}");
			}
		}
	}
}
