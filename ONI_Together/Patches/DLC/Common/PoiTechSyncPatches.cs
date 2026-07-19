using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.DLC.Common;

namespace ONI_Together.Patches.DLC.Common
{
	internal static class PoiTechSync
	{
		private static int _applyDepth;
		private static bool IsApplyingState => _applyDepth > 0;

		public static void ResetSessionState() => _applyDepth = 0;

		internal static bool ShouldRunGameplay(bool inSession, bool isHost, bool isApplying)
			=> !inSession || isHost || isApplying;

		internal static bool TryApplyRequest(PoiTechRequestPacket request)
		{
			if (request == null || !TryResolve(request.TargetNetId, out var smi))
				return false;
			if (request.Kind == PoiTechRequestKind.SetPendingChore)
			{
				bool current = GetPending(smi);
				if (GetUnlocked(smi) || current != request.ExpectedValue)
					return false;
				RunApplying(() => SetPending(smi, request.DesiredValue));
				return true;
			}

			bool seen = GetSeen(smi);
			if (!GetUnlocked(smi) || seen != request.ExpectedValue)
				return false;
			RunApplying(() => smi.sm.seenNotification.Set(request.DesiredValue, smi));
			return true;
		}

		internal static bool TryApply(PoiTechStatePacket state)
		{
			if (state == null || !state.IsWireValid() ||
			    !TryResolve(state.TargetNetId, out var smi))
				return false;
			RunApplying(() => ApplyState(smi, state));
			return true;
		}

		private static void ApplyState(POITechItemUnlocks.Instance smi, PoiTechStatePacket state)
		{
			if (!state.PendingChore)
				SetPending(smi, false);
			if (state.IsUnlocked && !GetUnlocked(smi))
				smi.UnlockTechItems();
			smi.sm.isUnlocked.Set(state.IsUnlocked, smi);
			if (state.PendingChore)
				SetPending(smi, true);
			smi.sm.seenNotification.Set(state.SeenNotification, smi);
		}

		private static void SetPending(POITechItemUnlocks.Instance smi, bool desired)
		{
			var traverse = Traverse.Create(smi);
			bool hasChore = traverse.Field("unlockChore").GetValue<Chore>() != null;
			smi.sm.pendingChore.Set(desired, smi);
			if (desired && !hasChore)
				traverse.Method("CreateChore").GetValue();
			else if (!desired && hasChore)
				traverse.Method("CancelChore").GetValue();
		}

		private static void RunApplying(System.Action action)
		{
			_applyDepth++;
			try
			{
				action();
			}
			finally
			{
				_applyDepth--;
			}
		}

		internal static void Broadcast(int targetNetId)
		{
			if (TryResolve(targetNetId, out var smi))
				Broadcast(smi);
		}

		internal static void Broadcast(POITechItemUnlocks.Instance smi)
		{
			if (!MultiplayerSession.IsHostInSession || smi == null)
				return;
			int targetNetId = smi.gameObject.GetNetIdentity()?.NetId ?? 0;
			var state = Capture(smi, targetNetId);
			if (state.IsWireValid())
				PacketSender.SendToAllClients(state, PacketSendMode.ReliableImmediate);
		}

		private static PoiTechStatePacket Capture(POITechItemUnlocks.Instance smi, int netId)
			=> new()
			{
				TargetNetId = netId,
				IsUnlocked = GetUnlocked(smi),
				PendingChore = GetPending(smi),
				SeenNotification = GetSeen(smi)
			};

		private static bool TryResolve(int netId, out POITechItemUnlocks.Instance smi)
		{
			smi = null;
			if (!NetworkIdentityRegistry.TryGet(netId, out var identity))
				return false;
			smi = identity.gameObject.GetSMI<POITechItemUnlocks.Instance>();
			return smi != null;
		}

		internal static bool GetPending(POITechItemUnlocks.Instance smi)
			=> smi.sm.pendingChore.Get(smi);

		private static bool GetUnlocked(POITechItemUnlocks.Instance smi)
			=> smi.sm.isUnlocked.Get(smi);

		private static bool GetSeen(POITechItemUnlocks.Instance smi)
			=> smi.sm.seenNotification.Get(smi);

		[HarmonyPatch(typeof(POITechItemUnlocks.Instance),
			nameof(POITechItemUnlocks.Instance.OnSidescreenButtonPressed))]
		internal static class ButtonPatch
		{
			internal static bool Prefix(POITechItemUnlocks.Instance __instance)
			{
				if (!MultiplayerSession.IsClient)
					return true;
				int netId = __instance.gameObject.GetNetIdentity()?.NetId ?? 0;
				bool pending = GetPending(__instance);
				if (netId != 0)
					PacketSender.SendToAllOtherPeers(new PoiTechRequestPacket
					{
						TargetNetId = netId,
						Kind = PoiTechRequestKind.SetPendingChore,
						ExpectedValue = pending,
						DesiredValue = !pending
					});
				return false;
			}

			internal static void Postfix(POITechItemUnlocks.Instance __instance)
				=> Broadcast(__instance);
		}

		[HarmonyPatch(typeof(POITechItemUnlockWorkable), "OnCompleteWork", typeof(WorkerBase))]
		internal static class CompleteWorkPatch
		{
			internal static bool Prefix()
				=> ShouldRunGameplay(MultiplayerSession.InSession,
					MultiplayerSession.IsHost, IsApplyingState);

			internal static void Postfix(POITechItemUnlockWorkable __instance)
				=> Broadcast(__instance.GetSMI<POITechItemUnlocks.Instance>());
		}

		[HarmonyPatch(typeof(POITechItemUnlocks.Instance),
			nameof(POITechItemUnlocks.Instance.UnlockTechItems))]
		internal static class UnlockPatch
		{
			internal static bool Prefix()
				=> ShouldRunGameplay(MultiplayerSession.InSession,
					MultiplayerSession.IsHost, IsApplyingState);
		}

		[HarmonyPatch(typeof(POITechItemUnlocks), "OnNotificationAknowledged", typeof(object))]
		internal static class NotificationPatch
		{
			internal static void Postfix(object o)
			{
				if (o is not UnityEngine.GameObject target)
					return;
				var smi = target.GetSMI<POITechItemUnlocks.Instance>();
				if (smi == null)
					return;
				if (MultiplayerSession.IsHost)
				{
					Broadcast(smi);
					return;
				}
				if (!MultiplayerSession.IsClient)
					return;
				int netId = target.GetNetIdentity()?.NetId ?? 0;
				if (netId != 0)
					PacketSender.SendToAllOtherPeers(new PoiTechRequestPacket
					{
						TargetNetId = netId,
						Kind = PoiTechRequestKind.AcknowledgeNotification,
						ExpectedValue = false,
						DesiredValue = true
					});
			}
		}
	}
}
