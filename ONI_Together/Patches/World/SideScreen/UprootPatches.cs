using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;

namespace ONI_Together.Patches.World.SideScreen
{
	[HarmonyPatch(typeof(Uprootable), nameof(Uprootable.MarkForUproot))]
	public static class Uprootable_MarkForUproot_Patch
	{
		public static void Postfix(Uprootable __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InActiveSession) return;
			if (__instance.IsNullOrDestroyed()) return;

			int cell = Grid.PosToCell(__instance.gameObject);

			var packet = new BuildingConfigPacket
			{
				NetId = 0,
				Cell = cell,
				ConfigHash = "UprootPlant".GetHashCode(),
				Value = 1f,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}
}
