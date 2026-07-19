using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.DuplicantActions;
using Shared.Profiling;

namespace ONI_Together.Patches.Duplicant
{
	// Sync personal priorities (the 0-9 matrix)
	[HarmonyPatch(typeof(ChoreConsumer), "SetPersonalPriority")]
	public static class ChoreConsumerPatch
	{
		public static bool Prefix(ChoreConsumer __instance, ChoreGroup group, int value)
		{
			if (!MultiplayerSession.InSession || MultiplayerSession.IsHost
			    || DuplicantPriorityPacket.IsApplying)
				return true;
			if (__instance == null || group == null)
				return false;

			var identity = __instance.GetComponent<NetworkIdentity>();
			if (identity == null || !DuplicantPriorityPacket.IsValidRequest(identity.NetId, group.Id, value))
				return false;

			PacketSender.SendToHost(new DuplicantPriorityPacket
			{
				NetId = identity.NetId,
				ChoreGroupId = group.Id,
				Priority = value
			});
			return false;
		}

		public static void Postfix(ChoreConsumer __instance, ChoreGroup group, int value)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InSession) return;

			// Check if we are currently applying a packet to avoid loops
			if (DuplicantPriorityPacket.IsApplying) return;

			var identity = __instance.GetComponent<NetworkIdentity>();
			if (identity != null)
			{
				var packet = new DuplicantPriorityPacket
				{
					NetId = identity.NetId,
					ChoreGroupId = group.Id,
					Priority = value
				};

				if (MultiplayerSession.IsHost)
					PacketSender.SendToAllClients(packet);

				DebugConsole.Log($"[ChoreConsumerPatch] Sent priority update for {identity.name}: {group.Id} = {value}");
			}
		}
	}
}
