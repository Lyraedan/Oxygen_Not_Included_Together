using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Tools;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;

namespace ONI_Together.Patches.World
{
	[HarmonyPatch(typeof(Prioritizable), "SetMasterPriority")]
	public static class PrioritizablePatch
	{
		public static void Postfix(Prioritizable __instance, PrioritySetting priority)
		{
			using var _ = Profiler.Scope();

			if (PrioritizeStatePacket.IsApplying) return;
			if (DragToolPacket.ProcessingIncoming) return;
			if (!MultiplayerSession.InActiveSession) return;
			if (__instance == null || __instance.gameObject == null) return;

			// Find or register NetId
			var identity = __instance.gameObject.GetNetIdentity();
			int netId = identity != null ? identity.NetId : 0;
			int cell = Grid.PosToCell(__instance.gameObject);

			var packet = new PrioritizeStatePacket();
			packet.Priorities.Add(new PrioritizeStatePacket.PriorityData
			{
				NetId = netId,
				Cell = cell,
				PriorityClass = (int)priority.priority_class,
				PriorityValue = priority.priority_value
			});

			if (MultiplayerSession.IsHost)
				PacketSender.SendToAllClients(packet);
			else
				PacketSender.SendToHost(packet);
		}
	}
}
