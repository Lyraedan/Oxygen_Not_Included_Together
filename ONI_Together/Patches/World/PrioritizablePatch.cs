using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Tools.Prioritize;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World
{
	[HarmonyPatch(typeof(Prioritizable), "SetMasterPriority")]
	public static class PrioritizablePatch
	{
		public static void Postfix(Prioritizable __instance, PrioritySetting priority)
		{
			using var _ = Profiler.Scope();

			if (PrioritizeStatePacket.IsApplying || !MultiplayerSession.InSession ||
			    !MultiplayerSession.IsHost)
				return;

			// Find NetId
			int netId = 0;
			// Prioritizable is a component, usually on the same GameObject as NetworkIdentity
			var identity = __instance.GetComponent<NetworkIdentity>();
			if (identity != null)
			{
				netId = identity.NetId;
			}

			if (netId != 0)
			{
				var packet = new PrioritizeStatePacket();
				packet.Priorities.Add(new PrioritizeStatePacket.PriorityData
				{
					NetId = netId,
					PriorityClass = (int)priority.priority_class,
					PriorityValue = priority.priority_value
				});

				PacketSender.SendToAllClients(packet);
			}
		}
	}

	[HarmonyPatch(typeof(UserMenuScreen), "OnPriorityClicked")]
	public static class UserMenuPriorityPatch
	{
		public static bool Prefix(UserMenuScreen __instance, PrioritySetting priority)
		{
			if (!MultiplayerSession.InSession || MultiplayerSession.IsHost || PrioritizeStatePacket.IsApplying)
				return true;
			if (!PriorityAuthority.IsValidClientPriority(priority))
				return false;

			GameObject selected = Traverse.Create(__instance).Field("selected").GetValue<GameObject>();
			NetworkIdentity identity = selected?.GetComponent<NetworkIdentity>();
			Prioritizable prioritizable = selected?.GetComponent<Prioritizable>();
			if (identity == null || identity.NetId == 0 || prioritizable == null || !prioritizable.IsPrioritizable())
				return false;

			PacketSender.SendToAllOtherPeers(new PrioritizeTargetRequestPacket
			{
				NetId = identity.NetId,
				PriorityClass = (int)priority.priority_class,
				PriorityValue = priority.priority_value
			});
			return false;
		}
	}
}
