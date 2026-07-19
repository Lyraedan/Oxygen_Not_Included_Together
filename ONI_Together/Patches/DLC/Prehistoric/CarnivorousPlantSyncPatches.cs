using HarmonyLib;
using System.Collections.Generic;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.DLC.Prehistoric;
using UnityEngine;

namespace ONI_Together.Patches.DLC.Prehistoric
{
	internal readonly struct CarnivorousVictimCapture
	{
		internal readonly int VictimNetId;
		internal readonly string PrefabId;

		internal CarnivorousVictimCapture(int victimNetId, string prefabId)
		{
			VictimNetId = victimNetId;
			PrefabId = prefabId;
		}
	}

	internal static class CarnivorousPlantSync
	{
		internal const int MaxPendingStates = 256;
		internal const float PendingLifetimeSeconds = 120f;
		private sealed class PendingState
		{
			internal CarnivorousPlantStatePacket Packet;
			internal float ExpiresAt;
		}
		private static readonly Dictionary<int, PendingState> PendingStates = new();
		private static int applyDepth;
		private static bool _retryScheduled;
		private static int _retryGeneration;
		internal static bool IsApplying => applyDepth > 0;
		internal static int PendingCount => PendingStates.Count;

		public static void ResetSessionState()
		{
			PendingStates.Clear();
			applyDepth = 0;
			_retryScheduled = false;
			_retryGeneration++;
		}

		internal static bool ShouldRunGameplay(bool inSession, bool isHost, bool applying)
			=> applying || !inSession || isHost;

		internal static CarnivorousVictimCapture CaptureVictim(GameObject victim)
		{
			if (victim == null)
				return default;
			NetworkIdentity identity = victim.AddOrGet<NetworkIdentity>();
			if (identity.NetId == 0)
				identity.RegisterIdentity();
			return new CarnivorousVictimCapture(identity.NetId, victim.PrefabID().ToString());
		}

		internal static void Send(CarnivorousPlantKind kind, GameObject plant,
			CarnivorousVictimCapture victim, bool hasEaten)
		{
			if (!MultiplayerSession.IsHostInSession || plant == null)
				return;
			NetworkIdentity identity = plant.AddOrGet<NetworkIdentity>();
			if (identity.NetId == 0)
				identity.RegisterIdentity();
			var packet = new CarnivorousPlantStatePacket
			{
				Kind = kind,
				PlantNetId = identity.NetId,
				VictimNetId = hasEaten ? victim.VictimNetId : 0,
				HasEatenCreature = hasEaten,
				LastConsumedPrefabId = hasEaten ? victim.PrefabId : string.Empty
			};
			if (packet.IsWireValid())
				PacketSender.SendToAllClients(packet, PacketSendMode.ReliableImmediate);
		}

		internal static bool TryApply(CarnivorousPlantStatePacket packet)
		{
			if (packet == null || !packet.IsWireValid())
				return false;
			if (!TryResolve(packet, out FlytrapConsumptionMonitor flytrap,
			    out CritterTrapPlant critterTrap, out GameObject victim))
			{
				QueuePending(packet, Time.unscaledTime);
				ScheduleRetry();
				return false;
			}

			applyDepth++;
			try
			{
				if (packet.Kind == CarnivorousPlantKind.Flytrap)
					ApplyFlytrap(packet, flytrap);
				else
					ApplyCritterTrap(packet, critterTrap);
				if (victim != null)
					Util.KDestroyGameObject(victim);
			}
			finally { applyDepth--; }
			if (PendingStates.TryGetValue(packet.PlantNetId, out PendingState pending) &&
			    ReferenceEquals(pending.Packet, packet))
				PendingStates.Remove(packet.PlantNetId);
			return true;
		}

		internal static bool CanMutate(bool plantResolved) => plantResolved;

		internal static void QueuePending(CarnivorousPlantStatePacket packet, float now)
		{
			PrunePending(now);
			if (PendingStates.TryGetValue(packet.PlantNetId, out PendingState pending))
			{
				if (!ReferenceEquals(pending.Packet, packet))
				{
					pending.Packet = packet;
					pending.ExpiresAt = now + PendingLifetimeSeconds;
				}
				return;
			}
			if (PendingStates.Count >= MaxPendingStates)
				EvictOldestPending();
			PendingStates[packet.PlantNetId] = new PendingState
			{
				Packet = packet,
				ExpiresAt = now + PendingLifetimeSeconds
			};
		}

		internal static CarnivorousPlantStatePacket GetPending(int plantNetId, float now)
		{
			PrunePending(now);
			return PendingStates.TryGetValue(plantNetId, out var pending) ? pending.Packet : null;
		}

		internal static void RetryPending()
		{
			PrunePending(Time.unscaledTime);
			foreach (PendingState pending in new List<PendingState>(PendingStates.Values))
				TryApply(pending.Packet);
		}

		private static bool TryResolve(CarnivorousPlantStatePacket packet,
			out FlytrapConsumptionMonitor flytrap, out CritterTrapPlant critterTrap, out GameObject victim)
		{
			flytrap = null;
			critterTrap = null;
			victim = null;
			bool plantResolved = packet.Kind == CarnivorousPlantKind.Flytrap
				? NetworkIdentityRegistry.TryGetComponent(packet.PlantNetId, out flytrap) && flytrap?.smi != null
				: NetworkIdentityRegistry.TryGetComponent(packet.PlantNetId, out critterTrap) && critterTrap?.smi != null;
			if (packet.VictimNetId != 0 && NetworkIdentityRegistry.TryGet(packet.VictimNetId, out var identity))
				victim = identity?.gameObject;
			return CanMutate(plantResolved);
		}

		private static void PrunePending(float now)
		{
			foreach (int netId in new List<int>(PendingStates.Keys))
				if (PendingStates[netId].ExpiresAt < now)
					PendingStates.Remove(netId);
		}

		private static void EvictOldestPending()
		{
			int oldestId = 0;
			float oldestExpiry = float.MaxValue;
			foreach (KeyValuePair<int, PendingState> entry in PendingStates)
				if (entry.Value.ExpiresAt < oldestExpiry)
				{
					oldestId = entry.Key;
					oldestExpiry = entry.Value.ExpiresAt;
				}
			PendingStates.Remove(oldestId);
		}

		private static void ScheduleRetry()
		{
			if (_retryScheduled || GameScheduler.Instance == null)
				return;
			_retryScheduled = true;
			int generation = _retryGeneration;
			GameScheduler.Instance.Schedule("CarnivorousPlant pending state", 0.1f, _ =>
			{
				if (generation != _retryGeneration)
					return;
				_retryScheduled = false;
				RetryPending();
			});
		}

		private static void ApplyFlytrap(CarnivorousPlantStatePacket packet,
			FlytrapConsumptionMonitor monitor)
		{
			Traverse.Create(monitor.smi).Field("lastConsumedEntityPrefabID").SetValue(packet.LastConsumedPrefabId);
			if (packet.HasEatenCreature && !monitor.smi.HasEaten)
				monitor.smi.sm.EatSignal.Trigger(monitor.smi);
			if (monitor.smi.HasEaten != packet.HasEatenCreature)
				monitor.smi.sm.HasEaten.Set(packet.HasEatenCreature, monitor.smi);
		}

		private static void ApplyCritterTrap(CarnivorousPlantStatePacket packet, CritterTrapPlant plant)
		{
			bool current = plant.smi.sm.hasEatenCreature.Get(plant.smi);
			if (packet.HasEatenCreature && !current)
				plant.smi.sm.trapTriggered.Trigger(plant.smi);
			plant.smi.lastConsumedEntityPrefabID = packet.LastConsumedPrefabId;
			if (plant.smi.sm.hasEatenCreature.Get(plant.smi) != packet.HasEatenCreature)
				plant.smi.sm.hasEatenCreature.Set(packet.HasEatenCreature, plant.smi);
		}
	}

	[HarmonyPatch(typeof(FlytrapConsumptionMonitor.Instance),
		nameof(FlytrapConsumptionMonitor.Instance.OnPickupableLayerObjectDetected))]
	internal static class FlytrapVictimPatch
	{
		internal static bool Prefix(FlytrapConsumptionMonitor.Instance __instance, object obj,
			ref CarnivorousVictimCapture __state)
		{
			if (!CarnivorousPlantSync.ShouldRunGameplay(
			    MultiplayerSession.InSession, MultiplayerSession.IsHost, CarnivorousPlantSync.IsApplying))
				return false;
			if (MultiplayerSession.IsHostInSession && obj is Pickupable pickupable &&
			    __instance.master.IsEntityEdible(pickupable.gameObject))
				__state = CarnivorousPlantSync.CaptureVictim(pickupable.gameObject);
			return true;
		}

		internal static void Postfix(FlytrapConsumptionMonitor.Instance __instance,
			CarnivorousVictimCapture __state)
		{
			if (__instance?.HasEaten == true && __state.VictimNetId != 0)
				CarnivorousPlantSync.Send(CarnivorousPlantKind.Flytrap, __instance.gameObject, __state, true);
		}
	}

	[HarmonyPatch(typeof(FlytrapConsumptionMonitor), nameof(FlytrapConsumptionMonitor.BecomeHungry))]
	internal static class FlytrapHungryPatch
	{
		internal static void Postfix(FlytrapConsumptionMonitor.Instance smi)
			=> CarnivorousPlantSync.Send(CarnivorousPlantKind.Flytrap, smi?.gameObject, default, false);
	}

	[HarmonyPatch(typeof(TrapTrigger), nameof(TrapTrigger.OnCreatureOnTrap))]
	internal static class CritterTrapVictimPatch
	{
		internal static bool Prefix(object data, ref CarnivorousVictimCapture __state)
		{
			if (!CarnivorousPlantSync.ShouldRunGameplay(
			    MultiplayerSession.InSession, MultiplayerSession.IsHost, CarnivorousPlantSync.IsApplying))
				return false;
			if (MultiplayerSession.IsHostInSession && data is Trappable trappable)
				__state = CarnivorousPlantSync.CaptureVictim(trappable.gameObject);
			return true;
		}

		internal static void Postfix(TrapTrigger __instance, CarnivorousVictimCapture __state)
		{
			CritterTrapPlant plant = __instance?.GetComponent<CritterTrapPlant>();
			if (plant?.smi != null && __state.VictimNetId != 0 &&
			    plant.smi.sm.hasEatenCreature.Get(plant.smi))
				CarnivorousPlantSync.Send(CarnivorousPlantKind.CritterTrap, __instance.gameObject, __state, true);
		}
	}
}
