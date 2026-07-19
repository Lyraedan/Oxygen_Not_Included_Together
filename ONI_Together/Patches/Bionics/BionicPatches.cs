using System;
using System.Collections.Generic;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.DLC.Bionic;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.Patches.Bionics
{
    internal class BionicPatches
    {
        // Crude bionic patches, TODO ensure this gets synced properly

        [HarmonyPatch(typeof(BionicOilMonitor.Instance), nameof(BionicOilMonitor.Instance.ReportOilTankFilled))]
        public static class BionicOilMonitor_ReportOilTankFilled_Patch
        {
            static bool Prefix(BionicOilMonitor.Instance __instance)
            {
                if (__instance == null)
                    return false;

                if (__instance.sm == null || __instance.sm.OilFilledSignal == null)
                    return false;

                if (__instance.gameObject == null)
                    return false;

                return true;
            }
        }

        [HarmonyPatch(typeof(BionicOilMonitor.Instance), nameof(BionicOilMonitor.Instance.ReportOilRanOut))]
        public static class BionicOilMonitor_ReportOilRanOut_Patch
        {
            static bool Prefix(BionicOilMonitor.Instance __instance)
            {
                if (__instance == null)
                    return false;

                if (__instance.sm == null || __instance.sm.OilRanOutSignal == null)
                    return false;

                if (__instance.gameObject == null)
                    return false;

                return true;
            }
        }

        [HarmonyPatch(typeof(BionicOilMonitor.Instance), nameof(BionicOilMonitor.Instance.ReportOilValueChanged))]
        public static class BionicOilMonitor_ReportOilValueChanged_Patch
        {
            static bool Prefix(BionicOilMonitor.Instance __instance, float delta)
            {
                if (__instance == null)
                    return false;

                if (__instance.sm == null || __instance.sm.OilValueChanged == null)
                    return false;

                if (__instance.gameObject == null)
                    return false;

                if (__instance.OnOilValueChanged == null) return false;

                return true;
            }
        }

        [HarmonyPatch(typeof(BionicOilMonitor.Instance), nameof(BionicOilMonitor.Instance.StartSM))]
        public static class BionicOilMonitor_Instance_StartSM_Patch
        {
            static bool Prefix(BionicOilMonitor.Instance __instance)
            {
                if (__instance == null)
                    return false;

                // This is what crashes in noOil.Enter
                if (__instance.effects == null)
                    return false;

                if (__instance.resume == null)
                    return false;

                if (__instance.gameObject == null)
                    return false;

                return true; // allow SM start
            }
        }

        [HarmonyPatch(typeof(BionicOilMonitor.Instance), nameof(BionicOilMonitor.Instance.GetEffect))]
        public static class BionicOilMonitor_Instance_GetEffect_Patch
        {
            static bool Prefix(BionicOilMonitor.Instance __instance, ref string __result)
            {
                if (__instance?.resume == null)
                {
                    __result = "NoLubricationMajor";
                    return false;
                }
                return true;
            }
        }
    }

	internal static class BionicSyncGuard
	{
		private static int _applyDepth;
		internal static bool IsApplying => _applyDepth > 0;

		public static void ResetSessionState() => _applyDepth = 0;

		internal static void Run(System.Action action)
		{
			bool previousAssignmentApply = AssignmentPacket.IsApplying;
			_applyDepth++;
			AssignmentPacket.IsApplying = true;
			try
			{
				action();
			}
			finally
			{
				AssignmentPacket.IsApplying = previousAssignmentApply;
				_applyDepth--;
			}
		}
	}

	internal static class BionicUpgradeAssignmentSync
	{
		internal static bool TryCapture(BionicUpgradeComponent upgrade, out BionicAssignmentData data)
		{
			data = null;
			int upgradeNetId = upgrade?.GetNetIdentity()?.NetId ?? 0;
			if (upgradeNetId == 0)
				return false;

			IAssignableIdentity assignee = Traverse.Create(upgrade).Field("assignee")
				.GetValue<IAssignableIdentity>();
			bool hasAssignee = assignee != null && !assignee.IsNull();
			int assigneeNetId = 0;
			if (hasAssignee && !TryGetAssigneeNetId(assignee, out assigneeNetId))
				return false;

			data = new BionicAssignmentData
			{
				UpgradeNetId = upgradeNetId,
				HasAssignee = hasAssignee,
				AssigneeNetId = assigneeNetId
			};
			return data.IsWireValid();
		}

		internal static bool TryApply(BionicAssignmentData data)
		{
			if (data == null || !data.IsWireValid() ||
			    !NetworkIdentityRegistry.TryGetComponent(data.UpgradeNetId, out BionicUpgradeComponent upgrade) ||
			    upgrade == null)
				return false;

			if (!data.HasAssignee)
			{
				if (!upgrade.IsAssigned())
					return true;
				BionicSyncGuard.Run(upgrade.Unassign);
				return true;
			}

			if (!TryResolveAssignee(data.AssigneeNetId, out IAssignableIdentity assignee) ||
			    !upgrade.CanAssignTo(assignee))
				return false;
			if (upgrade.IsAssignedTo(assignee))
				return true;
			BionicSyncGuard.Run(() => upgrade.Assign(assignee));
			return true;
		}

		internal static void SendRequest(BionicUpgradeComponent upgrade, IAssignableIdentity assignee)
		{
			if (!TryBuildData(upgrade, assignee, out BionicAssignmentData data))
				return;
			PacketSender.SendToAllOtherPeers(new BionicAssignmentRequestPacket(data));
		}

		internal static void SendState(BionicUpgradeComponent upgrade)
		{
			if (BionicSyncGuard.IsApplying || !MultiplayerSession.InSession || !MultiplayerSession.IsHost ||
			    !TryCapture(upgrade, out BionicAssignmentData data))
				return;
			PacketSender.SendToAllClients(new BionicAssignmentStatePacket(data));
		}

		private static bool TryBuildData(
			BionicUpgradeComponent upgrade,
			IAssignableIdentity assignee,
			out BionicAssignmentData data)
		{
			data = null;
			int upgradeNetId = upgrade?.GetNetIdentity()?.NetId ?? 0;
			bool hasAssignee = assignee != null && !assignee.IsNull();
			int assigneeNetId = 0;
			if (upgradeNetId == 0 || (hasAssignee && !TryGetAssigneeNetId(assignee, out assigneeNetId)))
				return false;
			data = new BionicAssignmentData
			{
				UpgradeNetId = upgradeNetId,
				HasAssignee = hasAssignee,
				AssigneeNetId = assigneeNetId
			};
			return data.IsWireValid();
		}

		private static bool TryGetAssigneeNetId(IAssignableIdentity assignee, out int netId)
		{
			netId = 0;
			UnityEngine.GameObject target = assignee is MinionAssignablesProxy proxy
				? proxy.GetTargetGameObject()
				: (assignee as KMonoBehaviour)?.gameObject;
			netId = target?.GetNetIdentity()?.NetId ?? 0;
			return netId != 0;
		}

		private static bool TryResolveAssignee(int netId, out IAssignableIdentity assignee)
		{
			assignee = null;
			if (!NetworkIdentityRegistry.TryGet(netId, out var identity) || identity?.gameObject == null)
				return false;

			MinionIdentity minion = identity.GetComponent<MinionIdentity>();
			if (minion != null)
			{
				MinionAssignablesProxy proxy = minion.GetSoleOwner()?.GetComponent<MinionAssignablesProxy>();
				assignee = proxy ?? (IAssignableIdentity)minion;
				return true;
			}

			StoredMinionIdentity stored = identity.GetComponent<StoredMinionIdentity>();
			if (stored != null)
			{
				assignee = stored;
				return true;
			}
			return false;
		}
	}

	internal static class ExplorerGeyserRevealSync
	{
		private static readonly HashSet<string> AppliedOutcomes = new(StringComparer.Ordinal);
		private static BionicUpgrade_ExplorerBoosterMonitor.Instance _capturing;
		private static bool _outcomeSent;

		public static void ResetSessionState()
		{
			AppliedOutcomes.Clear();
			_capturing = null;
			_outcomeSent = false;
		}

		internal static bool TryClaimOutcome(string key) => AppliedOutcomes.Add(key);

		internal static void Begin(BionicUpgrade_ExplorerBoosterMonitor.Instance smi)
		{
			_capturing = smi;
			_outcomeSent = false;
		}

		internal static void End()
		{
			_capturing = null;
			_outcomeSent = false;
		}

		internal static void CaptureReveal(int x, int y)
		{
			if (_capturing == null || _outcomeSent || !MultiplayerSession.InSession || !MultiplayerSession.IsHost)
				return;
			int explorerNetId = _capturing.gameObject.GetNetIdentity()?.NetId ?? 0;
			var packet = new ExplorerGeyserRevealPacket
			{
				ExplorerNetId = explorerNetId,
				WorldId = _capturing.GetMyWorldId(),
				Cell = Grid.XYToCell(x, y)
			};
			if (!packet.IsWireValid())
				return;
			_outcomeSent = true;
			PacketSender.SendToAllClients(packet);
		}

		internal static bool TryApply(ExplorerGeyserRevealPacket packet)
		{
			if (packet == null || !packet.IsWireValid() || !Grid.IsValidCell(packet.Cell) ||
			    Grid.WorldIdx[packet.Cell] != packet.WorldId ||
			    !NetworkIdentityRegistry.TryGet(packet.ExplorerNetId, out var identity) ||
			    identity?.gameObject == null || identity.gameObject.GetMyWorldId() != packet.WorldId)
				return false;

			string key = BuildOutcomeKey(packet.ExplorerNetId, packet.WorldId, packet.Cell);
			if (!TryClaimOutcome(key))
				return true;

			Grid.CellToXY(packet.Cell, out int x, out int y);
			GridVisibility.Reveal(x, y, 4, 4f);
			BionicUpgrade_ExplorerBoosterMonitor.Instance smi =
				identity.gameObject.GetSMI<BionicUpgrade_ExplorerBoosterMonitor.Instance>();
			if (smi != null)
			{
				Notifier notifier = smi.gameObject.AddOrGet<Notifier>();
				Notification notification = smi.GetGeyserDiscoveredNotification();
				int cell = packet.Cell;
				notification.customClickCallback = _ => GameUtil.FocusCamera(cell);
				notifier.Add(notification);
			}
			return true;
		}

		internal static string BuildOutcomeKey(int explorerNetId, int worldId, int cell)
			=> $"{explorerNetId}:{worldId}:{cell}";
	}

	internal sealed class BionicAssignmentPatchState
	{
		internal bool OwnsAssignmentGuard;
		internal bool PreviousAssignmentApply;
	}

	[HarmonyPatch(typeof(BionicUpgradeComponent), nameof(BionicUpgradeComponent.Assign), typeof(IAssignableIdentity))]
	internal static class BionicUpgradeAssignPatch
	{
		internal static bool Prefix(
			BionicUpgradeComponent __instance,
			IAssignableIdentity new_assignee,
			out BionicAssignmentPatchState __state)
		{
			__state = new BionicAssignmentPatchState();
			if (BionicSyncGuard.IsApplying || !MultiplayerSession.InSession)
				return true;
			if (MultiplayerSession.IsClient)
			{
				BionicUpgradeAssignmentSync.SendRequest(__instance, new_assignee);
				return false;
			}
			__state.OwnsAssignmentGuard = true;
			__state.PreviousAssignmentApply = AssignmentPacket.IsApplying;
			AssignmentPacket.IsApplying = true;
			return true;
		}

		internal static Exception Finalizer(
			Exception __exception,
			BionicUpgradeComponent __instance,
			BionicAssignmentPatchState __state)
		{
			if (__state?.OwnsAssignmentGuard == true)
			{
				AssignmentPacket.IsApplying = __state.PreviousAssignmentApply;
				if (__exception == null)
					BionicUpgradeAssignmentSync.SendState(__instance);
			}
			return __exception;
		}
	}

	[HarmonyPatch(typeof(BionicUpgradeComponent), nameof(BionicUpgradeComponent.Unassign))]
	internal static class BionicUpgradeUnassignPatch
	{
		internal static bool Prefix(BionicUpgradeComponent __instance, out BionicAssignmentPatchState __state)
		{
			__state = new BionicAssignmentPatchState();
			if (BionicSyncGuard.IsApplying || !MultiplayerSession.InSession)
				return true;
			if (MultiplayerSession.IsClient)
			{
				BionicUpgradeAssignmentSync.SendRequest(__instance, null);
				return false;
			}
			__state.OwnsAssignmentGuard = true;
			__state.PreviousAssignmentApply = AssignmentPacket.IsApplying;
			AssignmentPacket.IsApplying = true;
			return true;
		}

		internal static Exception Finalizer(
			Exception __exception,
			BionicUpgradeComponent __instance,
			BionicAssignmentPatchState __state)
		{
			if (__state?.OwnsAssignmentGuard == true)
			{
				AssignmentPacket.IsApplying = __state.PreviousAssignmentApply;
				if (__exception == null)
					BionicUpgradeAssignmentSync.SendState(__instance);
			}
			return __exception;
		}
	}

	[HarmonyPatch(typeof(BionicUpgrade_ExplorerBoosterMonitor),
		nameof(BionicUpgrade_ExplorerBoosterMonitor.RevealUndiscoveredGeyser))]
	internal static class ExplorerBoosterRevealPatch
	{
		internal static bool Prefix(BionicUpgrade_ExplorerBoosterMonitor.Instance smi)
		{
			if (!MultiplayerSession.InSession)
				return true;
			if (MultiplayerSession.IsClient)
				return false;
			ExplorerGeyserRevealSync.Begin(smi);
			return true;
		}

		internal static Exception Finalizer(Exception __exception)
		{
			ExplorerGeyserRevealSync.End();
			return __exception;
		}
	}

	[HarmonyPatch(typeof(GridVisibility), nameof(GridVisibility.Reveal),
		typeof(int), typeof(int), typeof(int), typeof(float))]
	internal static class ExplorerGridRevealCapturePatch
	{
		internal static void Postfix(int __0, int __1)
			=> ExplorerGeyserRevealSync.CaptureReveal(__0, __1);
	}
}
