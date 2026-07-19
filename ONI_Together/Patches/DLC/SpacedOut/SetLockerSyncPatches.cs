using System.Collections.Generic;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.DLC.SpacedOut;

namespace ONI_Together.Patches.DLC.SpacedOut
{
	internal static class SetLockerSync
	{
		private static int _applyDepth;
		private static bool IsApplyingState => _applyDepth > 0;

		public static void ResetSessionState() => _applyDepth = 0;

		internal static bool ShouldRunGameplay(bool inSession, bool isHost, bool isApplying)
			=> !inSession || isHost || isApplying;

		internal static bool TrySetPending(int netId, bool expected, bool desired)
		{
			if (!TryResolve(netId, out SetLocker locker) || GetUsed(locker) ||
			    GetPending(locker) != expected || expected == desired)
				return false;
			RunApplying(() => SetPending(locker, desired));
			return true;
		}

		internal static bool TryApply(SetLockerStatePacket state)
		{
			if (state == null || !state.IsWireValid() ||
			    !TryResolve(state.TargetNetId, out SetLocker locker))
				return false;
			RunApplying(() => ApplyState(locker, state));
			return true;
		}

		private static void ApplyState(SetLocker locker, SetLockerStatePacket state)
		{
			SetPending(locker, state.PendingRummage);
			var traverse = Traverse.Create(locker);
			traverse.Field("contents").SetValue(state.Contents.ToArray());
			traverse.Field("used").SetValue(state.Used);
			traverse.Field("pendingRummage").SetValue(state.PendingRummage);
			ApplyPhase(locker, state.Phase);
			Game.Instance?.userMenu?.Refresh(locker.gameObject);
		}

		private static void SetPending(SetLocker locker, bool desired)
		{
			Chore chore = Traverse.Create(locker).Field("chore").GetValue<Chore>();
			if (desired && chore == null)
				locker.ActivateChore();
			else if (!desired && chore != null)
				locker.CancelChore();
			Traverse.Create(locker).Field("pendingRummage").SetValue(desired);
		}

		private static void ApplyPhase(SetLocker locker, SetLockerPhase phase)
		{
			if (locker.smi == null)
				return;
			StateMachine.BaseState target = phase switch
			{
				SetLockerPhase.BeingWorked => locker.smi.sm.being_worked,
				SetLockerPhase.Open => locker.smi.sm.open,
				SetLockerPhase.Off => locker.smi.sm.off,
				_ => locker.smi.sm.closed
			};
			if (!locker.smi.IsInsideState(target))
				locker.smi.GoTo(target);
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

		internal static void Broadcast(int netId)
		{
			if (TryResolve(netId, out SetLocker locker))
				Broadcast(locker);
		}

		internal static void Broadcast(SetLocker locker)
		{
			if (!MultiplayerSession.IsHostInSession || locker == null)
				return;
			int netId = locker.GetNetIdentity()?.NetId ?? 0;
			SetLockerStatePacket state = Capture(locker, netId);
			if (state.IsWireValid())
				PacketSender.SendToAllClients(state, PacketSendMode.ReliableImmediate);
		}

		private static SetLockerStatePacket Capture(SetLocker locker, int netId)
		{
			string[] contents = Traverse.Create(locker).Field("contents").GetValue<string[]>();
			return new SetLockerStatePacket
			{
				TargetNetId = netId,
				PendingRummage = GetPending(locker),
				Used = GetUsed(locker),
				Phase = GetPhase(locker),
				Contents = contents == null ? new List<string>() : new List<string>(contents)
			};
		}

		private static SetLockerPhase GetPhase(SetLocker locker)
		{
			if (locker.smi == null || locker.smi.IsInsideState(locker.smi.sm.closed))
				return SetLockerPhase.Closed;
			if (locker.smi.IsInsideState(locker.smi.sm.being_worked))
				return SetLockerPhase.BeingWorked;
			return locker.smi.IsInsideState(locker.smi.sm.open)
				? SetLockerPhase.Open : SetLockerPhase.Off;
		}

		private static bool TryResolve(int netId, out SetLocker locker)
			=> NetworkIdentityRegistry.TryGetComponent(netId, out locker);

		private static bool GetPending(SetLocker locker)
			=> Traverse.Create(locker).Field("pendingRummage").GetValue<bool>();

		private static bool GetUsed(SetLocker locker)
			=> Traverse.Create(locker).Field("used").GetValue<bool>();

		[HarmonyPatch(typeof(SetLocker), nameof(SetLocker.ChooseContents))]
		internal static class ChooseContentsPatch
		{
			internal static bool Prefix()
				=> ShouldRunGameplay(MultiplayerSession.InSession,
					MultiplayerSession.IsHost, IsApplyingState);
		}

		[HarmonyPatch(typeof(SetLocker), nameof(SetLocker.DropContents))]
		internal static class DropContentsPatch
		{
			internal static bool Prefix()
				=> ShouldRunGameplay(MultiplayerSession.InSession,
					MultiplayerSession.IsHost, IsApplyingState);
		}

		[HarmonyPatch(typeof(SetLocker), "CompleteChore")]
		internal static class CompleteChorePatch
		{
			internal static bool Prefix()
				=> ShouldRunGameplay(MultiplayerSession.InSession,
					MultiplayerSession.IsHost, IsApplyingState);

			internal static void Postfix(SetLocker __instance) => Broadcast(__instance);
		}

		[HarmonyPatch(typeof(SetLocker), "OnSpawn")]
		internal static class OnSpawnPatch
		{
			internal static void Postfix(SetLocker __instance) => Broadcast(__instance);
		}

		[HarmonyPatch(typeof(SetLocker), nameof(SetLocker.OnSidescreenButtonPressed))]
		internal static class ButtonPatch
		{
			internal static bool Prefix(SetLocker __instance)
			{
				if (!MultiplayerSession.IsClient)
					return true;
				int netId = __instance.GetNetIdentity()?.NetId ?? 0;
				bool pending = GetPending(__instance);
				if (netId != 0)
					PacketSender.SendToAllOtherPeers(new SetLockerRequestPacket
					{
						TargetNetId = netId,
						ExpectedPending = pending,
						DesiredPending = !pending
					});
				return false;
			}

			internal static void Postfix(SetLocker __instance) => Broadcast(__instance);
		}
	}
}
