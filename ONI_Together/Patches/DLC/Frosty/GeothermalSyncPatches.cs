using System;
using System.Collections.Generic;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.Frosty;

namespace ONI_Together.Patches.DLC.Frosty
{
	internal static class FrostySyncGuard
	{
		private static int _applyDepth;
		internal static bool IsApplying => _applyDepth > 0;

		public static void ResetSessionState() => _applyDepth = 0;

		internal static void Run(System.Action action)
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
	}

	internal static class GeothermalControllerSync
	{
		internal static bool TryCapture(
			GeothermalController controller,
			out GeothermalControllerStatePacket state)
		{
			state = null;
			int targetNetId = controller?.GetNetIdentity()?.NetId ?? 0;
			if (targetNetId == 0 || controller.smi == null || !TryGetPhase(controller.smi, out var phase))
				return false;
			state = new GeothermalControllerStatePacket
			{
				TargetNetId = targetNetId,
				Progress = controller.State,
				Phase = phase
			};
			return state.IsWireValid();
		}

		internal static bool TryApply(GeothermalControllerStatePacket state)
		{
			if (state == null || !state.IsWireValid() ||
			    !NetworkIdentityRegistry.TryGetComponent(state.TargetNetId, out GeothermalController controller) ||
			    controller?.smi == null || !TryResolvePhase(controller.smi, state.Phase, out StateMachine.BaseState phase))
				return false;

			if (controller.State == state.Progress && controller.smi.IsInsideState(phase))
				return true;

			FrostySyncGuard.Run(() =>
			{
				Traverse.Create(controller).Field("state").SetValue(state.Progress);
				if (!controller.smi.IsInsideState(phase))
					controller.smi.GoTo(phase);
			});
			return true;
		}

		internal static void SendButtonRequest(GeothermalController.StatesInstance smi)
		{
			GeothermalController controller = smi?.master;
			int targetNetId = controller?.GetNetIdentity()?.NetId ?? 0;
			if (targetNetId == 0 || !TryGetDesiredProgress(controller.State, out var desired))
				return;
			PacketSender.SendToAllOtherPeers(new GeothermalControllerRequestPacket(
				targetNetId, controller.State, desired));
		}

		private static bool TryGetDesiredProgress(
			GeothermalController.ProgressState current,
			out GeothermalController.ProgressState desired)
		{
			desired = current switch
			{
				GeothermalController.ProgressState.NOT_STARTED => GeothermalController.ProgressState.FETCHING_STEEL,
				GeothermalController.ProgressState.FETCHING_STEEL => GeothermalController.ProgressState.NOT_STARTED,
				GeothermalController.ProgressState.RECONNECTING_PIPES => GeothermalController.ProgressState.NOT_STARTED,
				GeothermalController.ProgressState.AT_CAPACITY => GeothermalController.ProgressState.COMPLETE,
				_ => current
			};
			return desired != current;
		}

		internal static void SendState(GeothermalController controller)
		{
			if (FrostySyncGuard.IsApplying || !MultiplayerSession.InSession || !MultiplayerSession.IsHost ||
			    !TryCapture(controller, out GeothermalControllerStatePacket state))
				return;
			PacketSender.SendToAllClients(state);
		}

		internal static bool TryGetPhase(
			GeothermalController.StatesInstance smi,
			out GeothermalControllerPhase phase)
		{
			phase = default;
			if (smi == null)
				return false;
			var sm = smi.sm;
			if (smi.IsInsideState(sm.offline.initial)) phase = GeothermalControllerPhase.OfflineInitial;
			else if (smi.IsInsideState(sm.offline.fetchSteel)) phase = GeothermalControllerPhase.OfflineFetchSteel;
			else if (smi.IsInsideState(sm.offline.checkSupplies)) phase = GeothermalControllerPhase.OfflineCheckSupplies;
			else if (smi.IsInsideState(sm.offline.reconnectPipes)) phase = GeothermalControllerPhase.OfflineReconnectPipes;
			else if (smi.IsInsideState(sm.offline.notifyRepaired)) phase = GeothermalControllerPhase.OfflineNotifyRepaired;
			else if (smi.IsInsideState(sm.offline.repaired)) phase = GeothermalControllerPhase.OfflineRepaired;
			else if (smi.IsInsideState(sm.offline.filling)) phase = GeothermalControllerPhase.OfflineFilling;
			else if (smi.IsInsideState(sm.offline.filled.ready)) phase = GeothermalControllerPhase.OfflineFilledReady;
			else if (smi.IsInsideState(sm.offline.filled.obstructed)) phase = GeothermalControllerPhase.OfflineFilledObstructed;
			else if (smi.IsInsideState(sm.online.active)) phase = GeothermalControllerPhase.OnlineActive;
			else if (smi.IsInsideState(sm.online.venting.pre)) phase = GeothermalControllerPhase.OnlineVentingPre;
			else if (smi.IsInsideState(sm.online.venting.loop)) phase = GeothermalControllerPhase.OnlineVentingLoop;
			else if (smi.IsInsideState(sm.online.venting.post)) phase = GeothermalControllerPhase.OnlineVentingPost;
			else if (smi.IsInsideState(sm.online.obstructed)) phase = GeothermalControllerPhase.OnlineObstructed;
			else return false;
			return true;
		}

		private static bool TryResolvePhase(
			GeothermalController.StatesInstance smi,
			GeothermalControllerPhase phase,
			out StateMachine.BaseState state)
		{
			var sm = smi.sm;
			state = phase switch
			{
				GeothermalControllerPhase.OfflineInitial => sm.offline.initial,
				GeothermalControllerPhase.OfflineFetchSteel => sm.offline.fetchSteel,
				GeothermalControllerPhase.OfflineCheckSupplies => sm.offline.checkSupplies,
				GeothermalControllerPhase.OfflineReconnectPipes => sm.offline.reconnectPipes,
				GeothermalControllerPhase.OfflineNotifyRepaired => sm.offline.notifyRepaired,
				GeothermalControllerPhase.OfflineRepaired => sm.offline.repaired,
				GeothermalControllerPhase.OfflineFilling => sm.offline.filling,
				GeothermalControllerPhase.OfflineFilledReady => sm.offline.filled.ready,
				GeothermalControllerPhase.OfflineFilledObstructed => sm.offline.filled.obstructed,
				GeothermalControllerPhase.OnlineActive => sm.online.active,
				GeothermalControllerPhase.OnlineVentingPre => sm.online.venting.pre,
				GeothermalControllerPhase.OnlineVentingLoop => sm.online.venting.loop,
				GeothermalControllerPhase.OnlineVentingPost => sm.online.venting.post,
				GeothermalControllerPhase.OnlineObstructed => sm.online.obstructed,
				_ => null
			};
			return state != null;
		}
	}

	internal static class GeothermalVentSync
	{
		internal static bool TryCapture(GeothermalVent vent, out GeothermalVentStatePacket state)
		{
			state = null;
			int targetNetId = vent?.GetNetIdentity()?.NetId ?? 0;
			if (targetNetId == 0)
				return false;

			var traverse = Traverse.Create(vent);
			List<GeothermalVent.ElementInfo> material = traverse.Field("availableMaterial")
				.GetValue<List<GeothermalVent.ElementInfo>>();
			GeothermalVent.EmitterInfo emitter = traverse.Field("emitterInfo").GetValue<GeothermalVent.EmitterInfo>();
			if (material == null || material.Count > GeothermalVentStatePacket.MaxMaterialCount)
				return false;

			state = new GeothermalVentStatePacket
			{
				TargetNetId = targetNetId,
				RecentMass = Math.Max(0f, traverse.Field("recentMass").GetValue<float>()),
				HasEmitterElement = emitter.element.mass > 0f,
				EmitterElement = emitter.element.mass > 0f ? ToState(emitter.element) : null,
				AvailableMaterial = new List<GeothermalElementState>(material.Count)
			};
			foreach (GeothermalVent.ElementInfo element in material)
				state.AvailableMaterial.Add(ToState(element));
			return state.IsWireValid();
		}

		internal static bool TryApply(GeothermalVentStatePacket state)
		{
			if (state == null || !state.IsWireValid() ||
			    !NetworkIdentityRegistry.TryGetComponent(state.TargetNetId, out GeothermalVent vent) || vent == null)
				return false;

			var material = new List<GeothermalVent.ElementInfo>(state.AvailableMaterial.Count);
			foreach (GeothermalElementState element in state.AvailableMaterial)
			{
				if (!TryToElementInfo(element, out GeothermalVent.ElementInfo info))
					return false;
				material.Add(info);
			}

			var traverse = Traverse.Create(vent);
			GeothermalVent.EmitterInfo emitter = traverse.Field("emitterInfo").GetValue<GeothermalVent.EmitterInfo>();
			if (state.HasEmitterElement && !TryToElementInfo(state.EmitterElement, out emitter.element))
				return false;
			if (!state.HasEmitterElement)
				emitter.element = default;
			emitter.dirty = true;

			FrostySyncGuard.Run(() =>
			{
				traverse.Field("availableMaterial").SetValue(material);
				traverse.Field("recentMass").SetValue(state.RecentMass);
				traverse.Field("emitterInfo").SetValue(emitter);
			});
			return true;
		}

		internal static void SendState(GeothermalVent vent)
		{
			if (FrostySyncGuard.IsApplying || !MultiplayerSession.InSession || !MultiplayerSession.IsHost ||
			    !TryCapture(vent, out GeothermalVentStatePacket state))
				return;
			PacketSender.SendToAllClients(state);
		}

		private static GeothermalElementState ToState(GeothermalVent.ElementInfo info)
			=> new()
			{
				IsSolid = info.isSolid,
				Element = info.elementHash,
				Mass = info.mass,
				Temperature = info.temperature,
				DiseaseIndex = info.diseaseIdx,
				DiseaseCount = Math.Max(0, info.diseaseCount)
			};

		private static bool TryToElementInfo(GeothermalElementState state, out GeothermalVent.ElementInfo info)
		{
			info = default;
			Element element = ElementLoader.FindElementByHash(state.Element);
			if (element == null)
				return false;
			info.isSolid = element.IsSolid;
			info.elementHash = element.id;
			info.elementIdx = element.idx;
			info.mass = state.Mass;
			info.temperature = state.Temperature;
			info.diseaseIdx = state.DiseaseIndex;
			info.diseaseCount = state.DiseaseCount;
			return true;
		}
	}

	[HarmonyPatch]
	internal static class GeothermalControllerButtonPatch
	{
		internal static System.Reflection.MethodBase TargetMethod()
			=> AccessTools.Method(typeof(GeothermalController.StatesInstance),
				"ISidescreenButtonControl.OnSidescreenButtonPressed");

		internal static bool Prefix(GeothermalController.StatesInstance __instance)
		{
			if (FrostySyncGuard.IsApplying || !MultiplayerSession.InSession || MultiplayerSession.IsHost)
				return true;
			GeothermalControllerSync.SendButtonRequest(__instance);
			return false;
		}

		internal static void Postfix(GeothermalController.StatesInstance __instance)
		{
			if (MultiplayerSession.IsHost)
				GeothermalControllerSync.SendState(__instance?.master);
		}
	}

	[HarmonyPatch(typeof(GeothermalController), nameof(GeothermalController.PushToVents), new Type[] { })]
	internal static class GeothermalControllerPushPatch
	{
		internal static bool Prefix()
			=> !MultiplayerSession.InSession || !MultiplayerSession.IsClient;
	}

	[HarmonyPatch(typeof(GeothermalVent), "RecomputeEmissions")]
	internal static class GeothermalVentRecomputePatch
	{
		internal static bool Prefix()
			=> FrostySyncGuard.IsApplying || !MultiplayerSession.InSession || !MultiplayerSession.IsClient;

		internal static void Postfix(GeothermalVent __instance)
			=> GeothermalVentSync.SendState(__instance);
	}

	[HarmonyPatch(typeof(GeothermalVent), nameof(GeothermalVent.EmitSolidChunk))]
	internal static class GeothermalVentSolidChunkPatch
	{
		internal static bool Prefix()
			=> FrostySyncGuard.IsApplying || !MultiplayerSession.InSession || !MultiplayerSession.IsClient;

		internal static void Postfix(GeothermalVent __instance)
			=> GeothermalVentSync.SendState(__instance);
	}

	[HarmonyPatch(typeof(GeothermalVent), nameof(GeothermalVent.addMaterial))]
	internal static class GeothermalVentAddMaterialPatch
	{
		internal static void Postfix(GeothermalVent __instance)
			=> GeothermalVentSync.SendState(__instance);
	}
}
