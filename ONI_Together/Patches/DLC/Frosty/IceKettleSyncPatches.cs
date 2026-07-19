using System;
using System.Collections.Generic;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.Frosty;
using UnityEngine;

namespace ONI_Together.Patches.DLC.Frosty
{
	internal static class IceKettleSync
	{
		internal const int MaxPendingStates = 256;
		internal const int MaxPendingExhausts = 256;
		internal const float PendingLifetimeSeconds = 120f;
		private sealed class PendingState
		{
			internal IceKettleStatePacket Packet;
			internal float ExpiresAt;
		}
		private sealed class PendingExhaust
		{
			internal IceKettleExhaustPacket Packet;
			internal float ExpiresAt;
		}
		private static readonly HashSet<(int TargetNetId, ulong Sequence)> AppliedExhausts = new();
		private static readonly Dictionary<int, long> HostRevisions = new();
		private static readonly Dictionary<int, long> AppliedRevisions = new();
		private static readonly Dictionary<int, PendingState> PendingStates = new();
		private static readonly Dictionary<(int TargetNetId, ulong Sequence), PendingExhaust> PendingExhausts = new();
		private static ulong _nextExhaustSequence = (ulong)System.DateTime.UtcNow.Ticks;
		private static bool _retryScheduled;
		private static int _retryGeneration;
		internal static int PendingCount => PendingStates.Count;
		internal static int PendingExhaustCount => PendingExhausts.Count;

		public static void ResetSessionState()
		{
			AppliedExhausts.Clear();
			HostRevisions.Clear();
			AppliedRevisions.Clear();
			PendingStates.Clear();
			PendingExhausts.Clear();
			_nextExhaustSequence = (ulong)System.DateTime.UtcNow.Ticks;
			_retryScheduled = false;
			_retryGeneration++;
		}

		internal static bool ShouldRunGameplay(bool inSession, bool isHost, bool isApplying)
			=> !inSession || isHost || isApplying;

		internal static bool TryCapture(IceKettle.Instance kettle, out IceKettleStatePacket state)
		{
			state = null;
			int targetNetId = kettle?.gameObject.GetNetIdentity()?.NetId ?? 0;
			if (targetNetId == 0 || !TryGetStorages(kettle, out Storage[] storages) ||
			    !TryCaptureStorage(storages[0], out IceKettleStorageState fuel) ||
			    !TryCaptureStorage(storages[1], out IceKettleStorageState solids) ||
			    !TryCaptureStorage(storages[2], out IceKettleStorageState output))
				return false;

			state = new IceKettleStatePacket
			{
				TargetNetId = targetNetId,
				Revision = NextHostRevision(targetNetId),
				MeltingTimer = kettle.sm.MeltingTimer.Get(kettle),
				FuelStorage = fuel,
				KettleStorage = solids,
				OutputStorage = output
			};
			return state.IsWireValid();
		}

		internal static bool TryApply(IceKettleStatePacket state)
		{
			if (state == null || !state.IsWireValid())
				return false;
			PrunePending(Time.unscaledTime);
			AppliedRevisions.TryGetValue(state.TargetNetId, out long appliedRevision);
			if (!NeedsApply(appliedRevision, state.Revision))
			{
				RemovePendingThrough(state.TargetNetId, appliedRevision);
				return true;
			}
			if (PendingStates.TryGetValue(state.TargetNetId, out PendingState latest) &&
			    latest.Packet.Revision > state.Revision)
				return true;
			if (!TryResolve(state, out IceKettle.Instance kettle, out Storage[] storages,
			    out List<GameObject>[] resolvedItems))
			{
				QueuePending(state, Time.unscaledTime);
				ScheduleRetry();
				return false;
			}

			FrostySyncGuard.Run(() =>
			{
				ApplyStorage(storages[0], state.FuelStorage, resolvedItems[0]);
				ApplyStorage(storages[1], state.KettleStorage, resolvedItems[1]);
				ApplyStorage(storages[2], state.OutputStorage, resolvedItems[2]);
				kettle.sm.MeltingTimer.Set(state.MeltingTimer, kettle);
				kettle.UpdateMeter();
			});
			AppliedRevisions[state.TargetNetId] = state.Revision;
			RemovePendingThrough(state.TargetNetId, state.Revision);
			return true;
		}

		internal static bool CanMutate(bool kettleResolved, bool storagesResolved)
			=> kettleResolved && storagesResolved;

		internal static bool NeedsApply(long appliedRevision, long incomingRevision)
			=> incomingRevision > appliedRevision;

		internal static void QueuePending(IceKettleStatePacket state, float now)
		{
			PrunePending(now);
			if (PendingStates.TryGetValue(state.TargetNetId, out PendingState pending))
			{
				if (state.Revision <= pending.Packet.Revision)
					return;
				pending.Packet = state;
				pending.ExpiresAt = now + PendingLifetimeSeconds;
				return;
			}
			if (PendingStates.Count >= MaxPendingStates)
				EvictOldestPending();
			PendingStates[state.TargetNetId] = new PendingState
			{
				Packet = state,
				ExpiresAt = now + PendingLifetimeSeconds
			};
		}

		internal static bool TryGetPendingRevision(int targetNetId, float now, out long revision)
		{
			PrunePending(now);
			if (PendingStates.TryGetValue(targetNetId, out var pending))
			{
				revision = pending.Packet.Revision;
				return true;
			}
			revision = 0;
			return false;
		}

		internal static void QueuePendingExhaust(IceKettleExhaustPacket packet, float now)
		{
			PrunePending(now);
			if (packet == null || !packet.IsWireValid())
				return;
			var key = (packet.TargetNetId, packet.Exhaust.Sequence);
			if (PendingExhausts.TryGetValue(key, out PendingExhaust pending))
			{
				pending.Packet = packet;
				return;
			}
			if (PendingExhausts.Count >= MaxPendingExhausts)
				EvictOldestPendingExhaust();
			PendingExhausts[key] = new PendingExhaust
			{
				Packet = packet,
				ExpiresAt = now + PendingLifetimeSeconds
			};
		}

		internal static bool TryGetPendingExhaust(int targetNetId, ulong sequence, float now)
		{
			PrunePending(now);
			return PendingExhausts.ContainsKey((targetNetId, sequence));
		}

		internal static bool TryMarkExhaustApplied(int targetNetId, ulong sequence)
			=> AppliedExhausts.Add((targetNetId, sequence));

		internal static bool TryApplyExhaust(IceKettleExhaustPacket packet)
		{
			if (packet == null || !packet.IsWireValid())
				return false;
			PrunePending(Time.unscaledTime);
			var key = (packet.TargetNetId, packet.Exhaust.Sequence);
			if (AppliedExhausts.Contains(key))
			{
				PendingExhausts.Remove(key);
				return true;
			}
			if (!NetworkIdentityRegistry.TryGet(packet.TargetNetId, out var identity) ||
			    identity?.gameObject?.GetSMI<IceKettle.Instance>() is not IceKettle.Instance kettle)
			{
				QueuePendingExhaust(packet, Time.unscaledTime);
				ScheduleRetry();
				return false;
			}

			FrostySyncGuard.Run(() => SimMessages.AddRemoveSubstance(
				Grid.PosToCell(kettle.gameObject), packet.Exhaust.Element, null,
				packet.Exhaust.Mass, packet.Exhaust.Temperature, byte.MaxValue, 0));
			TryMarkExhaustApplied(packet.TargetNetId, packet.Exhaust.Sequence);
			PendingExhausts.Remove(key);
			return true;
		}

		internal static void RetryPending()
		{
			PrunePending(Time.unscaledTime);
			foreach (PendingState pending in new List<PendingState>(PendingStates.Values))
				TryApply(pending.Packet);
			foreach (PendingExhaust pending in new List<PendingExhaust>(PendingExhausts.Values))
				TryApplyExhaust(pending.Packet);
		}

		internal static bool TryCaptureExhaust(
			IceKettle.Instance kettle,
			out IceKettleExhaustState exhaust)
		{
			exhaust = null;
			if (!TryGetStorages(kettle, out Storage[] storages) ||
			    !kettle.HasAtLeastOneBatchOfSolidsWaitingToMelt)
				return false;
			PrimaryElement solid = storages[1].FindFirst(kettle.def.targetElementTag)?.GetComponent<PrimaryElement>();
			PrimaryElement fuel = storages[0].FindFirst(kettle.def.fuelElementTag)?.GetComponent<PrimaryElement>();
			Element target = ElementLoader.GetElement(kettle.def.targetElementTag);
			Element element = ElementLoader.FindElementByHash(kettle.def.exhaust_tag);
			if (solid == null || fuel == null || target == null || element == null)
				return false;
			float fuelMass = Mathf.Min(kettle.GetUnitsOfFuelRequiredToMelt(
				target, kettle.def.KGToMeltPerBatch, solid.Temperature), kettle.FuelUnitsAvailable);
			float exhaustMass = fuelMass * kettle.def.ExhaustMassPerUnitOfLumber;
			if (exhaustMass <= 0f)
				return false;
			exhaust = new IceKettleExhaustState
			{
				Element = element.id,
				Mass = exhaustMass,
				Temperature = fuel.Temperature,
				Sequence = NextExhaustSequence()
			};
			return exhaust.IsWireValid();
		}

		internal static void SendState(IceKettle.Instance kettle)
		{
			if (FrostySyncGuard.IsApplying || !MultiplayerSession.InSession || !MultiplayerSession.IsHost ||
			    !TryCapture(kettle, out IceKettleStatePacket state))
				return;
			PacketSender.SendToAllClients(state, PacketSendMode.Unreliable);
		}

		internal static void SendExhaust(IceKettle.Instance kettle, IceKettleExhaustState exhaust)
		{
			int targetNetId = kettle?.gameObject.GetNetIdentity()?.NetId ?? 0;
			var packet = new IceKettleExhaustPacket { TargetNetId = targetNetId, Exhaust = exhaust };
			if (FrostySyncGuard.IsApplying || !MultiplayerSession.InSession || !MultiplayerSession.IsHost ||
			    !packet.IsWireValid())
				return;
			PacketSender.SendToAllClients(packet, PacketSendMode.ReliableImmediate);
		}

		private static bool TryResolve(IceKettleStatePacket state, out IceKettle.Instance kettle,
			out Storage[] storages, out List<GameObject>[] resolvedItems)
		{
			kettle = null;
			storages = null;
			resolvedItems = null;
			bool kettleResolved = NetworkIdentityRegistry.TryGet(state.TargetNetId, out var identity) &&
			                      (kettle = identity?.gameObject?.GetSMI<IceKettle.Instance>()) != null &&
			                      TryGetStorages(kettle, out storages);
			if (!kettleResolved)
				return false;
			resolvedItems = new List<GameObject>[3];
			bool storageResolved = TryResolveStorage(storages[0], state.FuelStorage, out resolvedItems[0]) &&
			                       TryResolveStorage(storages[1], state.KettleStorage, out resolvedItems[1]) &&
			                       TryResolveStorage(storages[2], state.OutputStorage, out resolvedItems[2]);
			return CanMutate(kettleResolved, storageResolved);
		}

		private static bool TryCaptureStorage(Storage storage, out IceKettleStorageState state)
		{
			state = new IceKettleStorageState();
			if (storage == null || storage.items.Count > IceKettleStorageState.MaxItems)
				return false;
			foreach (GameObject item in storage.items)
			{
				PrimaryElement primary = item?.GetComponent<PrimaryElement>();
				int netId = item?.GetNetIdentity()?.NetId ?? 0;
				if (primary == null || item.GetComponent<Pickupable>() == null || netId == 0)
					return false;
				state.Items.Add(new IceKettleItemState
				{
					NetId = netId,
					TagHash = item.PrefabID().GetHashCode(),
					Mass = primary.Mass,
					Temperature = primary.Temperature,
					DiseaseIndex = primary.DiseaseIdx,
					DiseaseCount = Math.Max(0, primary.DiseaseCount)
				});
			}
			return state.IsWireValid();
		}

		private static bool TryResolveStorage(Storage storage, IceKettleStorageState state,
			out List<GameObject> resolvedItems)
		{
			resolvedItems = new List<GameObject>(state.Items.Count);
			if (storage == null)
				return false;
			foreach (IceKettleItemState item in state.Items)
			{
				if (!NetworkIdentityRegistry.TryGet(item.NetId, out var identity) ||
				    identity?.gameObject == null || identity.gameObject.PrefabID().GetHashCode() != item.TagHash ||
				    !storage.items.Contains(identity.gameObject) || identity.gameObject.GetComponent<PrimaryElement>() == null ||
				    identity.gameObject.GetComponent<Pickupable>() == null)
					return false;
				resolvedItems.Add(identity.gameObject);
			}
			return true;
		}

		private static void ApplyStorage(Storage storage, IceKettleStorageState state,
			List<GameObject> resolvedItems)
		{
			var desiredIds = new HashSet<int>();
			for (int i = 0; i < state.Items.Count; i++)
			{
				IceKettleItemState item = state.Items[i];
				desiredIds.Add(item.NetId);
				ApplyPrimaryElement(resolvedItems[i].GetComponent<PrimaryElement>(), item);
			}
			for (int i = storage.items.Count - 1; i >= 0; i--)
			{
				GameObject item = storage.items[i];
				int netId = item?.GetComponent<Networking.Components.NetworkIdentity>()?.NetId ?? 0;
				if (desiredIds.Contains(netId))
					continue;
				storage.Remove(item, do_disease_transfer: false);
				Util.KDestroyGameObject(item);
			}
		}

		private static void ApplyPrimaryElement(PrimaryElement primary, IceKettleItemState state)
		{
			if (primary.Mass != state.Mass || primary.Temperature != state.Temperature)
				primary.SetMassTemperature(state.Mass, state.Temperature);
			if (primary.DiseaseIdx == state.DiseaseIndex && primary.DiseaseCount == state.DiseaseCount)
				return;
			if (primary.DiseaseCount > 0)
				primary.ModifyDiseaseCount(-primary.DiseaseCount, "ONI Together ice kettle sync");
			if (state.DiseaseCount > 0)
				primary.AddDisease(state.DiseaseIndex, state.DiseaseCount, "ONI Together ice kettle sync");
		}

		private static bool TryGetStorages(IceKettle.Instance kettle, out Storage[] storages)
		{
			storages = kettle?.gameObject.GetComponents<Storage>();
			return storages != null && storages.Length >= 3;
		}

		internal static ulong NextExhaustSequence()
		{
			if (_nextExhaustSequence == ulong.MaxValue)
				_nextExhaustSequence = 1;
			else
				_nextExhaustSequence++;
			return _nextExhaustSequence;
		}

		internal static long NextHostRevision(int targetNetId)
		{
			HostRevisions.TryGetValue(targetNetId, out long previous);
			long next = previous == long.MaxValue ? 1 : previous + 1;
			HostRevisions[targetNetId] = next;
			return next;
		}

		private static void RemovePendingThrough(int targetNetId, long revision)
		{
			if (PendingStates.TryGetValue(targetNetId, out PendingState pending) &&
			    pending.Packet.Revision <= revision)
				PendingStates.Remove(targetNetId);
		}

		private static void PrunePending(float now)
		{
			foreach (int netId in new List<int>(PendingStates.Keys))
				if (PendingStates[netId].ExpiresAt < now)
					PendingStates.Remove(netId);
			foreach ((int TargetNetId, ulong Sequence) key in
			         new List<(int TargetNetId, ulong Sequence)>(PendingExhausts.Keys))
				if (PendingExhausts[key].ExpiresAt < now)
					PendingExhausts.Remove(key);
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

		private static void EvictOldestPendingExhaust()
		{
			(int TargetNetId, ulong Sequence) oldestKey = default;
			float oldestExpiry = float.MaxValue;
			foreach (KeyValuePair<(int TargetNetId, ulong Sequence), PendingExhaust> entry in PendingExhausts)
				if (entry.Value.ExpiresAt < oldestExpiry)
				{
					oldestKey = entry.Key;
					oldestExpiry = entry.Value.ExpiresAt;
				}
			PendingExhausts.Remove(oldestKey);
		}

		private static void ScheduleRetry()
		{
			if (_retryScheduled || GameScheduler.Instance == null)
				return;
			_retryScheduled = true;
			int generation = _retryGeneration;
			GameScheduler.Instance.Schedule("IceKettle pending sync", 0.1f, _ =>
			{
				if (generation != _retryGeneration)
					return;
				_retryScheduled = false;
				RetryPending();
			});
		}
	}

	[HarmonyPatch(typeof(IceKettle), nameof(IceKettle.MeltingTimerUpdate),
		new[] { typeof(IceKettle.Instance), typeof(float) })]
	internal static class IceKettleMeltingTimerPatch
	{
		internal static bool Prefix()
			=> IceKettleSync.ShouldRunGameplay(
				MultiplayerSession.InSession, MultiplayerSession.IsHost, FrostySyncGuard.IsApplying);

		internal static void Postfix(IceKettle.Instance smi)
			=> IceKettleSync.SendState(smi);
	}

	[HarmonyPatch(typeof(IceKettle), nameof(IceKettle.MeltNextBatch),
		new[] { typeof(IceKettle.Instance) })]
	internal static class IceKettleMeltNextBatchPatch
	{
		internal static bool Prefix(IceKettle.Instance smi, out IceKettleExhaustState __state)
		{
			__state = null;
			if (MultiplayerSession.InSession && MultiplayerSession.IsHost)
				IceKettleSync.TryCaptureExhaust(smi, out __state);
			return IceKettleSync.ShouldRunGameplay(
				MultiplayerSession.InSession, MultiplayerSession.IsHost, FrostySyncGuard.IsApplying);
		}

		internal static void Postfix(IceKettle.Instance smi, IceKettleExhaustState __state)
		{
			IceKettleSync.SendState(smi);
			IceKettleSync.SendExhaust(smi, __state);
		}
	}
}
