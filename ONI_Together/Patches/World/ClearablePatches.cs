using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Tools.Clear;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World
{
	public static class ClearablePatches
	{
		[HarmonyPatch(typeof(Clearable), nameof(Clearable.MarkForClear))]
		public static class ClearableMarkForClearPatch
		{
			public static void Postfix(Clearable __instance, bool restoringFromSave, bool allowWhenStored)
			{
				using var _ = Profiler.Scope();

				if (restoringFromSave) return;
				if (ClearableActionPacket.ProcessingIncoming) return;
				if (ClearPacket.ProcessingIncoming) return;
				if (!MultiplayerSession.InActiveSession) return;
				if (__instance == null || __instance.gameObject == null) return;

				var identity = __instance.gameObject.GetNetIdentity();
				int netId = identity != null ? identity.NetId : 0;
				int cell = Grid.PosToCell(__instance.gameObject);

				var packet = new ClearableActionPacket
				{
					NetId = netId,
					Cell = cell,
					IsMarked = true
				};

				if (MultiplayerSession.IsHost)
					PacketSender.SendToAllClients(packet);
				else
					PacketSender.SendToHost(packet);
			}
		}

		[HarmonyPatch(typeof(Clearable), nameof(Clearable.CancelClearing))]
		public static class ClearableCancelClearingPatch
		{
			public static void Postfix(Clearable __instance)
			{
				using var _ = Profiler.Scope();

				if (ClearableActionPacket.ProcessingIncoming) return;
				if (ClearPacket.ProcessingIncoming) return;
				if (!MultiplayerSession.InActiveSession) return;
				if (__instance == null || __instance.gameObject == null) return;

				var identity = __instance.gameObject.GetNetIdentity();
				int netId = identity != null ? identity.NetId : 0;
				int cell = Grid.PosToCell(__instance.gameObject);

				var packet = new ClearableActionPacket
				{
					NetId = netId,
					Cell = cell,
					IsMarked = false
				};

				if (MultiplayerSession.IsHost)
					PacketSender.SendToAllClients(packet);
				else
					PacketSender.SendToHost(packet);
			}
		}
	}
}
