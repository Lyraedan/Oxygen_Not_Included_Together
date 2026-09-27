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
		/// <summary>
		/// While above zero, (un)marking an item sends no packet: pile merges (Clearable.OnAbsorb)
		/// re-mark piles the other side has no id for, and the clear tool is synced by cell already.
		/// </summary>
		internal static int SuppressItemPackets;

		[HarmonyPatch(typeof(Clearable), "OnAbsorb")]
		public static class ClearableOnAbsorbPatch
		{
			public static void Prefix()
			{
				SuppressItemPackets++;
			}

			public static void Finalizer()
			{
				SuppressItemPackets--;
			}
		}

		[HarmonyPatch(typeof(Clearable), nameof(Clearable.MarkForClear))]
		public static class ClearableMarkForClearPatch
		{
			public static void Prefix(bool ___isMarkedForClear, out bool __state) => __state = ___isMarkedForClear;

			public static void Postfix(Clearable __instance, bool restoringFromSave, bool allowWhenStored, bool __state, bool ___isMarkedForClear)
			{
				using var _ = Profiler.Scope();

				if (restoringFromSave) return;
				if (__state || !___isMarkedForClear) return; // already marked, or the game refused to mark it
				if (SuppressItemPackets > 0) return;
				if (ClearableActionPacket.ProcessingIncoming) return;
				if (ClearPacket.ProcessingIncoming) return;
				if (!MultiplayerSession.InActiveSession) return;
				if (__instance == null || __instance.gameObject == null) return;

				Send(__instance, true);
			}
		}

		[HarmonyPatch(typeof(Clearable), nameof(Clearable.CancelClearing))]
		public static class ClearableCancelClearingPatch
		{
			public static void Prefix(bool ___isMarkedForClear, out bool __state) => __state = ___isMarkedForClear;

			public static void Postfix(Clearable __instance, bool __state)
			{
				using var _ = Profiler.Scope();

				// The game calls CancelClearing on every item it stores or equips, marked or not.
				if (!__state) return;
				if (SuppressItemPackets > 0) return;
				if (ClearableActionPacket.ProcessingIncoming) return;
				if (ClearPacket.ProcessingIncoming) return;
				if (!MultiplayerSession.InActiveSession) return;
				if (__instance == null || __instance.gameObject == null) return;

				Send(__instance, false);
			}
		}

		private static void Send(Clearable clearable, bool marked)
		{
			var identity = clearable.gameObject.GetNetIdentity();
			int netId = identity != null ? identity.NetId : 0;
			int cell = Grid.PosToCell(clearable.gameObject);

			var packet = new ClearableActionPacket
			{
				NetId = netId,
				Cell = cell,
				IsMarked = marked
			};

			if (MultiplayerSession.IsHost)
				PacketSender.SendToAllClients(packet);
			else
				PacketSender.SendToHost(packet);
		}
	}
}
