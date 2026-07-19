using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.Aquatic;
using UnityEngine;

namespace ONI_Together.Patches.DLC.Aquatic
{
	internal static class AquaticSync
	{
		internal sealed class ShearingCapture
		{
			internal int ProductNetId;
			internal Vector2 ProductVelocity;
		}

		// ponytail: ranch completion is main-thread gameplay; use per-completion storage if Klei parallelizes it.
		private static ShearingCapture ActiveShearingCapture;

		public static void ResetSessionState() => ActiveShearingCapture = null;

		internal static bool ShouldRunAuthoritativeGameplay(bool inSession, bool isHost)
			=> !inSession || isHost;

		internal static MethodBase ResolveShearingCompletionMethod()
			=> AccessTools.DeclaredMethod(
				typeof(UnderwaterShearingStationConfig),
				"<DoPostConfigureComplete>b__4_2",
				new[] { typeof(GameObject), typeof(WorkerBase) });

		internal static bool TryCaptureGrowth(
			GameObject critter,
			out AquaticShearableAmount amountKind,
			out float growth)
		{
			amountKind = AquaticShearableAmount.ScaleGrowth;
			growth = 0f;
			if (critter == null)
				return false;

			ElementGrowthMonitor.Instance element = critter.GetSMI<ElementGrowthMonitor.Instance>();
			if (element != null)
			{
				amountKind = AquaticShearableAmount.ElementGrowth;
				growth = element.elementGrowth.value;
				return true;
			}

			ScaleGrowthMonitor.Instance scale = critter.GetSMI<ScaleGrowthMonitor.Instance>();
			if (scale != null)
			{
				growth = scale.scaleGrowth.value;
				return true;
			}

			WellFedShearable.Instance wellFed = critter.GetSMI<WellFedShearable.Instance>();
			if (wellFed == null)
				return false;
			growth = wellFed.scaleGrowth.value;
			return true;
		}

		internal static bool TryApplyGrowth(
			GameObject critter,
			AquaticShearableAmount amountKind,
			float growth)
		{
			if (critter == null)
				return false;

			if (amountKind == AquaticShearableAmount.ElementGrowth)
			{
				ElementGrowthMonitor.Instance element = critter.GetSMI<ElementGrowthMonitor.Instance>();
				if (element == null)
					return false;
				element.elementGrowth.value = growth;
				return true;
			}

			ScaleGrowthMonitor.Instance scale = critter.GetSMI<ScaleGrowthMonitor.Instance>();
			if (scale != null)
			{
				scale.scaleGrowth.value = growth;
				return true;
			}

			WellFedShearable.Instance wellFed = critter.GetSMI<WellFedShearable.Instance>();
			if (wellFed == null)
				return false;
			wellFed.scaleGrowth.value = growth;
			return true;
		}

		internal static ShearingCapture BeginShearingCapture()
		{
			if (!MultiplayerSession.IsHostInSession)
				return null;
			ActiveShearingCapture = new ShearingCapture();
			return ActiveShearingCapture;
		}

		internal static void RecordShearingProduct(GameObject product, Vector2 velocity)
		{
			if (ActiveShearingCapture == null || product == null)
				return;
			NetworkIdentity identity = product.GetNetIdentity();
			if (identity == null || identity.NetId == 0)
				return;
			ActiveShearingCapture.ProductNetId = identity.NetId;
			ActiveShearingCapture.ProductVelocity = velocity;
		}

		internal static void FinishShearing(GameObject critter, ShearingCapture capture)
		{
			try
			{
				if (capture == null || capture.ProductNetId == 0 || critter == null ||
			    !TryCaptureGrowth(critter, out AquaticShearableAmount amountKind, out float growth))
					return;

				NetworkIdentity critterIdentity = critter.GetNetIdentity();
				RanchableMonitor.Instance ranchable = critter.GetSMI<RanchableMonitor.Instance>();
				GameObject station = ranchable?.TargetRanchStation?.gameObject;
				if (critterIdentity == null || critterIdentity.NetId == 0)
					return;

				PacketSender.SendToAllClients(new AquaticShearingOutcomePacket
				{
					CritterNetId = critterIdentity.NetId,
					StationNetId = station?.GetNetIdentity()?.NetId ?? 0,
					AmountKind = amountKind,
					Growth = growth,
					ProductNetId = capture.ProductNetId,
					ProductVelocity = capture.ProductVelocity
				}, PacketSendMode.ReliableImmediate);
			}
			finally
			{
				ClearShearing(capture);
			}
		}

		internal static void ClearShearing(ShearingCapture capture)
		{
			if (ReferenceEquals(ActiveShearingCapture, capture))
				ActiveShearingCapture = null;
		}
	}

	internal static class UnderwaterVentSync
	{
		private static readonly Dictionary<(int WorldId, int Cell), int> HostBubbleSequence = new();

		public static void ResetSessionState() => HostBubbleSequence.Clear();

		internal static int NextBubbleSequence(int worldId, int cell, bool hasBubble)
		{
			var key = (worldId, cell);
			HostBubbleSequence.TryGetValue(key, out int sequence);
			if (hasBubble)
			{
				sequence = sequence == int.MaxValue ? 1 : sequence + 1;
				HostBubbleSequence[key] = sequence;
			}
			return sequence;
		}

		internal static void Reset(UnderwaterVent.Instance vent)
		{
			if (!TryGetKey(vent, out int worldId, out int cell))
				return;
			HostBubbleSequence.Remove((worldId, cell));
			UnderwaterVentStatePacket.ForgetBubbleSequence(worldId, cell);
		}

		internal static void SendState(UnderwaterVent.Instance vent, float bubbleDt = 0f)
		{
			if (!MultiplayerSession.IsHostInSession || !TryGetKey(vent, out int worldId, out int cell))
				return;

			var packet = new UnderwaterVentStatePacket
			{
				WorldId = worldId,
				Cell = cell,
				BuildUp = vent.BuildUpProgress,
				Phase = GetPhase(vent)
			};
			float bubbleMass = vent.def.data.BubbleMassRate * bubbleDt;
			bool hasBubble = bubbleMass >= 1E-09f;
			packet.BubbleSequence = NextBubbleSequence(worldId, cell, hasBubble);
			if (hasBubble)
			{
				packet.HasBubble = true;
				packet.BubbleElement = vent.def.data.BubbleElement;
				packet.BubblePosition = Grid.CellToPos(cell) + vent.def.data.BubbleSpawnOffset;
				packet.BubbleMass = bubbleMass;
				packet.BubbleTemperature = vent.def.data.BubbleTemp;
			}

			PacketSender.SendToAllClients(packet, PacketSendMode.ReliableImmediate);
		}

		internal static UnderwaterVentPhase GetPhase(UnderwaterVent.Instance vent)
		{
			if (vent.IsInsideState(vent.sm.off))
				return UnderwaterVentPhase.Off;
			if (vent.BuildUpProgress <= 0f && vent.IsInsideState(vent.sm.on.blocked))
				return UnderwaterVentPhase.Unblocking;
			if (vent.BuildUpProgress >= 1f || vent.IsInsideState(vent.sm.on.blocked.idle))
				return UnderwaterVentPhase.Blocked;
			if (vent.IsInsideState(vent.sm.on.blocked.unblock))
				return UnderwaterVentPhase.Unblocking;
			return UnderwaterVentPhase.Erupting;
		}

		internal static bool NeedsApply(
			float currentBuildUp,
			UnderwaterVentPhase currentPhase,
			float targetBuildUp,
			UnderwaterVentPhase targetPhase)
			=> currentBuildUp != targetBuildUp || currentPhase != targetPhase;

		internal static void ApplyState(
			UnderwaterVent.Instance vent,
			float buildUp,
			UnderwaterVentPhase phase)
		{
			if (!NeedsApply(vent.BuildUpProgress, GetPhase(vent), buildUp, phase))
				return;
			if (vent.BuildUpProgress != buildUp)
				vent.sm.BuildUp.Set(buildUp, vent);

			switch (phase)
			{
				case UnderwaterVentPhase.Off:
					if (!vent.IsInsideState(vent.sm.off))
						vent.GoTo(vent.sm.off);
					break;
				case UnderwaterVentPhase.Erupting:
					if (!vent.IsInsideState(vent.sm.on.erupting))
						vent.GoTo(vent.sm.on.erupting);
					break;
				case UnderwaterVentPhase.Blocked:
					if (!vent.IsInsideState(vent.sm.on.blocked.idle))
						vent.GoTo(vent.sm.on.blocked.idle);
					break;
				case UnderwaterVentPhase.Unblocking:
					if (!vent.IsInsideState(vent.sm.on.blocked.unblock))
						vent.GoTo(vent.sm.on.blocked.unblock);
					break;
			}
		}

		internal static UnderwaterVent.Instance FindVent(int worldId, int cell)
		{
			if (!Grid.IsValidCell(cell))
				return null;
			GameObject go = Grid.Objects[cell, (int)ObjectLayer.Building];
			UnderwaterVent.Instance vent = go?.GetSMI<UnderwaterVent.Instance>();
			return vent != null && vent.gameObject.GetMyWorldId() == worldId ? vent : null;
		}

		private static bool TryGetKey(UnderwaterVent.Instance vent, out int worldId, out int cell)
		{
			worldId = vent?.gameObject.GetMyWorldId() ?? -1;
			cell = vent == null ? Grid.InvalidCell : Grid.PosToCell(vent.gameObject);
			return worldId >= 0 && Grid.IsValidCell(cell);
		}
	}

	internal static class UnderwaterDrillSync
	{
		internal static void SendState(UnderwaterVentDrill.Instance drill)
		{
			if (!MultiplayerSession.IsHostInSession || drill == null)
				return;

			NetworkIdentity identity = drill.gameObject.GetNetIdentity();
			Storage storage = drill.gameObject.GetComponent<Storage>();
			if (identity == null || identity.NetId == 0 || storage == null)
				return;

			PacketSender.SendToAllClients(new UnderwaterDrillStatePacket
			{
				DrillNetId = identity.NetId,
				Progress = drill.DrillProgress,
				DiamondMass = storage.GetMassAvailable(drill.def.DiamondTag),
				Phase = GetPhase(drill)
			}, PacketSendMode.ReliableImmediate);
		}

		internal static UnderwaterDrillPhase GetPhase(UnderwaterVentDrill.Instance drill)
		{
			if (drill.IsInsideState(drill.sm.noOperational))
				return UnderwaterDrillPhase.Off;
			if (drill.IsInsideState(drill.sm.operational.missingDiamonds))
				return UnderwaterDrillPhase.MissingDiamonds;
			if (drill.IsInsideState(drill.sm.operational.working))
				return UnderwaterDrillPhase.Working;
			return UnderwaterDrillPhase.Idle;
		}

		internal static bool NeedsApply(
			float currentProgress,
			float currentDiamondMass,
			UnderwaterDrillPhase currentPhase,
			float targetProgress,
			float targetDiamondMass,
			UnderwaterDrillPhase targetPhase)
			=> currentProgress != targetProgress || currentDiamondMass != targetDiamondMass ||
			   currentPhase != targetPhase;

		internal static void ApplyState(
			UnderwaterVentDrill.Instance drill,
			float progress,
			float diamondMass,
			UnderwaterDrillPhase phase)
		{
			Storage storage = drill.gameObject.GetComponent<Storage>();
			if (storage == null || !NeedsApply(
				    drill.DrillProgress,
				    storage.GetMassAvailable(drill.def.DiamondTag),
				    GetPhase(drill),
				    progress,
				    diamondMass,
				    phase))
				return;

			if (drill.DrillProgress != progress)
				drill.sm.DrillProgress.Set(progress, drill);
			ApplyDiamondMass(storage, drill.def.DiamondTag, diamondMass);

			switch (phase)
			{
				case UnderwaterDrillPhase.Off:
					if (!drill.IsInsideState(drill.sm.noOperational))
						drill.GoTo(drill.sm.noOperational);
					break;
				case UnderwaterDrillPhase.Idle:
					if (!drill.IsInsideState(drill.sm.operational.idle))
						drill.GoTo(drill.sm.operational.idle);
					break;
				case UnderwaterDrillPhase.MissingDiamonds:
					if (!drill.IsInsideState(drill.sm.operational.missingDiamonds))
						drill.GoTo(drill.sm.operational.missingDiamonds);
					break;
				case UnderwaterDrillPhase.Working:
					if (!drill.IsInsideState(drill.sm.operational.working.loop))
						drill.GoTo(drill.sm.operational.working.loop);
					break;
			}
		}

		internal static bool ApplyDiamondMass(Storage storage, Tag diamondTag, float targetMass)
		{
			if (storage == null)
				return false;

			GameObject keeper = null;
			var extras = new List<GameObject>();
			foreach (GameObject item in storage.items)
			{
				if (item == null || !item.HasTag(diamondTag))
					continue;
				if (keeper == null)
					keeper = item;
				else
					extras.Add(item);
			}

			if (keeper == null)
				return targetMass == 0f;
			keeper.GetComponent<PrimaryElement>().Mass = targetMass;
			foreach (GameObject extra in extras)
				storage.ConsumeIgnoringDisease(extra);
			if (targetMass == 0f)
				storage.ConsumeIgnoringDisease(keeper);
			return true;
		}
	}

	[HarmonyPatch]
	internal static class UnderwaterShearingCompletionPatch
	{
		internal static MethodBase TargetMethod() => AquaticSync.ResolveShearingCompletionMethod();

		internal static bool Prefix(out AquaticSync.ShearingCapture __state)
		{
			__state = null;
			if (!AquaticSync.ShouldRunAuthoritativeGameplay(MultiplayerSession.InSession, MultiplayerSession.IsHost))
				return false;
			__state = AquaticSync.BeginShearingCapture();
			return true;
		}

		internal static void Postfix(GameObject __0, AquaticSync.ShearingCapture __state)
			=> AquaticSync.FinishShearing(__0, __state);

		internal static Exception Finalizer(Exception __exception, AquaticSync.ShearingCapture __state)
		{
			if (__exception != null)
				AquaticSync.ClearShearing(__state);
			return __exception;
		}
	}

	[HarmonyPatch(typeof(FallerComponents), nameof(FallerComponents.Add),
		new[] { typeof(GameObject), typeof(Vector2) })]
	internal static class AquaticShearingFallerPatch
	{
		internal static void Postfix(GameObject __0, Vector2 __1)
			=> AquaticSync.RecordShearingProduct(__0, __1);
	}

	[HarmonyPatch(typeof(UnderwaterVent.Instance), nameof(UnderwaterVent.Instance.StartSM))]
	internal static class UnderwaterVentStartPatch
	{
		internal static void Prefix(UnderwaterVent.Instance __instance) => UnderwaterVentSync.Reset(__instance);
		internal static void Postfix(UnderwaterVent.Instance __instance) => UnderwaterVentSync.SendState(__instance);
	}

	[HarmonyPatch(typeof(UnderwaterVent.Instance), nameof(UnderwaterVent.Instance.EruptionUpdate))]
	internal static class UnderwaterVentEruptionPatch
	{
		internal static bool Prefix()
			=> AquaticSync.ShouldRunAuthoritativeGameplay(MultiplayerSession.InSession, MultiplayerSession.IsHost);

		internal static void Postfix(UnderwaterVent.Instance __instance, float dt)
			=> UnderwaterVentSync.SendState(__instance, dt);
	}

	[HarmonyPatch(typeof(UnderwaterVent.Instance), nameof(UnderwaterVent.Instance.Unblock))]
	internal static class UnderwaterVentUnblockPatch
	{
		internal static bool Prefix()
			=> AquaticSync.ShouldRunAuthoritativeGameplay(MultiplayerSession.InSession, MultiplayerSession.IsHost);

		internal static void Postfix(UnderwaterVent.Instance __instance) => UnderwaterVentSync.SendState(__instance);
	}

	[HarmonyPatch(typeof(UnderwaterVent.Instance), nameof(UnderwaterVent.Instance.SpawnSolidDebri))]
	internal static class UnderwaterVentSolidDebrisPatch
	{
		internal static bool Prefix()
			=> AquaticSync.ShouldRunAuthoritativeGameplay(MultiplayerSession.InSession, MultiplayerSession.IsHost);

		internal static void Postfix(UnderwaterVent.Instance __instance) => UnderwaterVentSync.SendState(__instance);
	}

	[HarmonyPatch(typeof(UnderwaterVentDrill.Instance), nameof(UnderwaterVentDrill.Instance.DrillUpdate))]
	internal static class UnderwaterDrillUpdatePatch
	{
		internal static bool Prefix(ref bool __result)
		{
			if (AquaticSync.ShouldRunAuthoritativeGameplay(MultiplayerSession.InSession, MultiplayerSession.IsHost))
				return true;
			__result = false;
			return false;
		}
	}

	[HarmonyPatch(typeof(UnderwaterVentDrill.Instance), nameof(UnderwaterVentDrill.Instance.UpdateDiamondMeter))]
	internal static class UnderwaterDrillMeterPatch
	{
		internal static void Postfix(UnderwaterVentDrill.Instance __instance)
			=> UnderwaterDrillSync.SendState(__instance);
	}

	[HarmonyPatch(typeof(UnderwaterVentDrill.Instance), nameof(UnderwaterVentDrill.Instance.SetOperationalActiveFlag))]
	internal static class UnderwaterDrillOperationalPatch
	{
		internal static void Postfix(UnderwaterVentDrill.Instance __instance)
			=> UnderwaterDrillSync.SendState(__instance);
	}

	[HarmonyPatch(typeof(UnderwaterVentDrill.Instance), nameof(UnderwaterVentDrill.Instance.UnblockVent))]
	internal static class UnderwaterDrillUnblockPatch
	{
		internal static bool Prefix()
			=> AquaticSync.ShouldRunAuthoritativeGameplay(MultiplayerSession.InSession, MultiplayerSession.IsHost);

		internal static void Postfix(UnderwaterVentDrill.Instance __instance)
			=> UnderwaterDrillSync.SendState(__instance);
	}
}
