using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using Shared;
using Shared.Profiling;

namespace ONI_Together.Patches.World.SideScreen
{
	[HarmonyPatch(typeof(Uprootable), "OnSpawn")]
	public static class Uprootable_OnSpawn_NetworkIdentity_Patch
	{
		public static void Postfix(Uprootable __instance)
		{
			if (__instance.IsNullOrDestroyed()) return;
			int netId = NetIdHelper.GetDeterministicUprootableId(__instance.gameObject);
			if (netId == 0) return;

			NetworkIdentity identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.OverrideNetId(netId);
		}
	}

	[HarmonyPatch(typeof(Uprootable), nameof(Uprootable.MarkForUproot))]
	public static class Uprootable_MarkForUproot_Patch
	{
		public static bool Prefix(Uprootable __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket || !MultiplayerSession.InSession
			    || MultiplayerSession.IsHost)
				return true;
			if (__instance.IsNullOrDestroyed()) return false;

			Send(__instance);
			return false;
		}

		public static void Postfix(Uprootable __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket || !MultiplayerSession.InSession
			    || !MultiplayerSession.IsHost || __instance.IsNullOrDestroyed())
				return;

			Send(__instance);
		}

		private static void Send(Uprootable uprootable)
		{
			NetworkIdentity identity = uprootable.gameObject.AddOrGet<NetworkIdentity>();
			int stableId = NetIdHelper.GetDeterministicUprootableId(uprootable.gameObject);
			if (stableId == 0 || !identity.OverrideNetId(stableId)) return;

			int cell = Grid.PosToCell(uprootable.gameObject);

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = cell,
				ConfigHash = NetworkingHash.ForConfigKey("UprootPlant"),
				Value = 1f,
				ConfigType = BuildingConfigType.Boolean
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}
}
