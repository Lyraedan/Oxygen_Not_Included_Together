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

			// Find NetId
			int netId = -1;
			// Prioritizable is a component, usually on the same GameObject as NetworkIdentity
			var identity = __instance.GetComponent<NetworkIdentity>();
			if (identity != null)
			{
				netId = identity.NetId;
			}

			if (netId != -1)
			{
				var packet = new PrioritizeStatePacket();
				packet.Priorities.Add(new PrioritizeStatePacket.PriorityData
				{
					NetId = netId,
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
}
