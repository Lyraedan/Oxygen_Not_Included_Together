using HarmonyLib;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.World;
using Shared.Profiling;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.World.SideScreen
{
	[HarmonyPatch(typeof(Uprootable), nameof(Uprootable.MarkForUproot))]
	public static class Uprootable_MarkForUproot_Patch
	{
		public static void Postfix(Uprootable __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;
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
