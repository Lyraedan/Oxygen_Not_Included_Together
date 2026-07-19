using System.Collections.Generic;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.DLC.Bionic;
using UnityEngine;

namespace ONI_Together.Patches.DLC.Bionic
{
	internal static class BionicExplosionSync
	{
		internal const int MaxPendingVelocities = 256;
		internal const float PendingLifetimeSeconds = 120f;
		private sealed class PendingVelocity
		{
			internal BionicExplosionVelocityPacket Packet;
			internal float ExpiresAt;
		}
		private static readonly Dictionary<int, int> HostSequences = new();
		private static readonly Dictionary<int, int> AppliedSequences = new();
		private static readonly Dictionary<int, int> AppliedCorrectionSequences = new();
		private static readonly Dictionary<(int ExplosionNetId, int Sequence),
			PendingVelocity> PendingVelocities = new();
		private static BionicExplosionVelocityPacket _activeCapture;
		private static bool _retryScheduled;
		private static int _retryGeneration;
		internal static int PendingVelocityCount => PendingVelocities.Count;
		internal static bool HasActiveCapture => _activeCapture != null;

		public static void ResetSessionState()
		{
			HostSequences.Clear();
			AppliedSequences.Clear();
			AppliedCorrectionSequences.Clear();
			PendingVelocities.Clear();
			_activeCapture = null;
			_retryScheduled = false;
			_retryGeneration++;
		}

		internal static bool IsNewerSequence(int lastSequence, int incomingSequence)
			=> incomingSequence > lastSequence;

		internal static bool TryBeginHostCapture(SelfChargingElectrobank bank)
		{
			if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost || _activeCapture != null)
				return false;
			int netId = bank?.GetNetIdentity()?.NetId ?? 0;
			return BeginHostCapture(netId);
		}

		internal static bool BeginHostCapture(int netId)
		{
			if (netId == 0 || _activeCapture != null)
				return false;
			_activeCapture = new BionicExplosionVelocityPacket
			{
				ExplosionNetId = netId,
				Sequence = NextSequence(netId)
			};
			return true;
		}

		internal static void CaptureVelocity(GameObject target, Vector2 velocity)
		{
			if (_activeCapture == null || _activeCapture.Corrections.Count >=
			    BionicExplosionVelocityPacket.MaxCorrections)
				return;
			int targetNetId = target?.GetNetIdentity()?.NetId ?? 0;
			if (targetNetId == 0 || ContainsCorrection(targetNetId))
				return;
			var correction = new BionicExplosionVelocityCorrection
			{
				TargetNetId = targetNetId,
				Velocity = velocity
			};
			if (correction.IsWireValid(BionicExplosionVelocityPacket.MaxVelocity))
				_activeCapture.Corrections.Add(correction);
		}

		internal static bool TryCompleteHostCapture(out BionicSelfChargingExplosionPacket outcome,
			out BionicExplosionVelocityPacket velocities)
		{
			velocities = _activeCapture;
			_activeCapture = null;
			outcome = velocities == null ? null : new BionicSelfChargingExplosionPacket
			{
				NetId = velocities.ExplosionNetId,
				Sequence = velocities.Sequence
			};
			return outcome?.IsWireValid() == true;
		}

		internal static void CancelHostCapture() => _activeCapture = null;

		internal static bool TryApply(BionicSelfChargingExplosionPacket packet)
		{
			if (packet == null || !packet.IsWireValid())
				return false;
			AppliedSequences.TryGetValue(packet.NetId, out int lastSequence);
			if (!IsNewerSequence(lastSequence, packet.Sequence))
			{
				RetryPendingVelocities(packet.NetId);
				return true;
			}
			if (!NetworkIdentityRegistry.TryGetComponent(
				    packet.NetId, out SelfChargingElectrobank bank) || bank == null)
				return false;
			BionicRuntimeSync.ApplyOutcome(bank.Explode);
			AppliedSequences[packet.NetId] = packet.Sequence;
			RetryPendingVelocities(packet.NetId);
			return true;
		}

		internal static bool ShouldApplyCorrectionSequence(
			int appliedOutcomeSequence,
			int appliedCorrectionSequence,
			int incomingSequence)
			=> incomingSequence == appliedOutcomeSequence && incomingSequence > appliedCorrectionSequence;

		internal static bool TryApplyVelocities(BionicExplosionVelocityPacket packet)
		{
			if (packet == null || !packet.IsWireValid())
				return false;
			PrunePending(Time.unscaledTime);
			var key = (packet.ExplosionNetId, packet.Sequence);
			AppliedSequences.TryGetValue(packet.ExplosionNetId, out int outcomeSequence);
			AppliedCorrectionSequences.TryGetValue(packet.ExplosionNetId, out int correctionSequence);
			if (packet.Sequence <= correctionSequence)
			{
				PendingVelocities.Remove(key);
				return true;
			}
			if (outcomeSequence > packet.Sequence)
			{
				PendingVelocities.Remove(key);
				return true;
			}
			bool targetsResolved = TryResolveTargets(packet, out List<GameObject> targets);
			if (!CanMutateVelocities(outcomeSequence, correctionSequence, packet.Sequence, targetsResolved))
			{
				QueuePendingVelocity(packet, Time.unscaledTime);
				ScheduleRetry();
				return false;
			}
			for (int i = 0; i < packet.Corrections.Count; i++)
				ApplyAbsoluteVelocity(targets[i], packet.Corrections[i].Velocity);
			AppliedCorrectionSequences[packet.ExplosionNetId] = packet.Sequence;
			PendingVelocities.Remove(key);
			return true;
		}

		internal static bool CanMutateVelocities(int appliedOutcomeSequence,
			int appliedCorrectionSequence, int incomingSequence, bool targetsResolved)
			=> targetsResolved && ShouldApplyCorrectionSequence(
				appliedOutcomeSequence, appliedCorrectionSequence, incomingSequence);

		internal static void QueuePendingVelocity(BionicExplosionVelocityPacket packet, float now)
		{
			PrunePending(now);
			var key = (packet.ExplosionNetId, packet.Sequence);
			if (PendingVelocities.TryGetValue(key, out PendingVelocity pending))
			{
				pending.Packet = packet;
				return;
			}
			if (PendingVelocities.Count >= MaxPendingVelocities)
				EvictOldestPending();
			PendingVelocities[key] = new PendingVelocity
			{
				Packet = packet,
				ExpiresAt = now + PendingLifetimeSeconds
			};
		}

		internal static BionicExplosionVelocityPacket GetPendingVelocity(
			int explosionNetId, int sequence, float now)
		{
			PrunePending(now);
			return PendingVelocities.TryGetValue((explosionNetId, sequence), out var pending)
				? pending.Packet
				: null;
		}

		internal static void RetryPendingVelocities()
		{
			PrunePending(Time.unscaledTime);
			foreach (PendingVelocity pending in new List<PendingVelocity>(PendingVelocities.Values))
				TryApplyVelocities(pending.Packet);
		}

		internal static void Cleanup(int explosionNetId)
		{
			HostSequences.Remove(explosionNetId);
			AppliedSequences.Remove(explosionNetId);
			AppliedCorrectionSequences.Remove(explosionNetId);
			foreach (var key in new List<(int ExplosionNetId, int Sequence)>(PendingVelocities.Keys))
				if (key.ExplosionNetId == explosionNetId)
					PendingVelocities.Remove(key);
		}

		internal static int NextSequence(int netId)
		{
			HostSequences.TryGetValue(netId, out int previous);
			int next = previous == int.MaxValue ? 1 : previous + 1;
			HostSequences[netId] = next;
			return next;
		}

		private static bool ContainsCorrection(int targetNetId)
		{
			foreach (BionicExplosionVelocityCorrection correction in _activeCapture.Corrections)
			{
				if (correction.TargetNetId == targetNetId)
					return true;
			}
			return false;
		}

		private static bool TryResolveTargets(
			BionicExplosionVelocityPacket packet,
			out List<GameObject> targets)
		{
			targets = new List<GameObject>(packet.Corrections.Count);
			foreach (BionicExplosionVelocityCorrection correction in packet.Corrections)
			{
				if (!NetworkIdentityRegistry.TryGet(correction.TargetNetId, out var identity) ||
				    identity?.gameObject == null)
					return false;
				targets.Add(identity.gameObject);
			}
			return true;
		}

		private static void ApplyAbsoluteVelocity(GameObject target, Vector2 velocity)
		{
			if (GameComps.Fallers.Has(target))
				GameComps.Fallers.Remove(target);
			if (GameComps.Gravities.Has(target))
				GameComps.Gravities.Remove(target);
			GameComps.Fallers.Add(target, velocity);
		}

		private static void RetryPendingVelocities(int explosionNetId)
		{
			PrunePending(Time.unscaledTime);
			foreach (var entry in new List<KeyValuePair<(int ExplosionNetId, int Sequence),
			         PendingVelocity>>(PendingVelocities))
				if (entry.Key.ExplosionNetId == explosionNetId)
					TryApplyVelocities(entry.Value.Packet);
		}

		private static void PrunePending(float now)
		{
			foreach (var key in new List<(int ExplosionNetId, int Sequence)>(PendingVelocities.Keys))
				if (PendingVelocities[key].ExpiresAt < now)
					PendingVelocities.Remove(key);
		}

		private static void EvictOldestPending()
		{
			(int ExplosionNetId, int Sequence) oldestKey = default;
			float oldestExpiry = float.MaxValue;
			foreach (var entry in PendingVelocities)
				if (entry.Value.ExpiresAt < oldestExpiry)
				{
					oldestKey = entry.Key;
					oldestExpiry = entry.Value.ExpiresAt;
				}
			PendingVelocities.Remove(oldestKey);
		}

		private static void ScheduleRetry()
		{
			if (_retryScheduled || GameScheduler.Instance == null)
				return;
			_retryScheduled = true;
			int generation = _retryGeneration;
			GameScheduler.Instance.Schedule("BionicExplosion pending velocity", 0.1f, _ =>
			{
				if (generation != _retryGeneration)
					return;
				_retryScheduled = false;
				RetryPendingVelocities();
			});
		}
	}

	internal sealed class BionicExplosionPatchState
	{
		internal bool Capturing;
		internal bool Completed;
	}

	[HarmonyPatch(typeof(SelfChargingElectrobank), nameof(SelfChargingElectrobank.Explode))]
	internal static class SelfChargingElectrobankExplodePatch
	{
		internal static bool Prefix(
			SelfChargingElectrobank __instance,
			out BionicExplosionPatchState __state)
		{
			__state = new BionicExplosionPatchState();
			bool shouldRun = BionicRuntimeSync.ShouldRunExplosion(
				MultiplayerSession.InSession,
				MultiplayerSession.IsHost,
				BionicRuntimeSync.IsApplyingOutcome);
			if (shouldRun && MultiplayerSession.InSession && MultiplayerSession.IsHost &&
			    !BionicRuntimeSync.IsApplyingOutcome)
				__state.Capturing = BionicExplosionSync.TryBeginHostCapture(__instance);
			return shouldRun;
		}

		internal static void Postfix(BionicExplosionPatchState __state)
		{
			if (__state?.Capturing != true || !BionicExplosionSync.TryCompleteHostCapture(
			    out BionicSelfChargingExplosionPacket outcome,
			    out BionicExplosionVelocityPacket velocities))
				return;
			PacketSender.SendToAllClients(outcome);
			if (velocities.Corrections.Count > 0 && velocities.IsWireValid())
				PacketSender.SendToAllClients(velocities);
			__state.Completed = true;
		}

		internal static System.Exception Finalizer(
			System.Exception __exception,
			BionicExplosionPatchState __state)
		{
			if (__state?.Capturing == true && !__state.Completed)
				BionicExplosionSync.CancelHostCapture();
			return __exception;
		}
	}

	[HarmonyPatch(typeof(FallerComponents), nameof(FallerComponents.Add),
		typeof(GameObject), typeof(Vector2))]
	internal static class BionicExplosionFallerCapturePatch
	{
		internal static void Postfix(GameObject go, Vector2 initial_velocity)
			=> BionicExplosionSync.CaptureVelocity(go, initial_velocity);
	}

	[HarmonyPatch(typeof(SelfChargingElectrobank), "OnCleanUp")]
	internal static class SelfChargingElectrobankCleanupPatch
	{
		internal static void Prefix(SelfChargingElectrobank __instance, out int __state)
			=> __state = __instance?.GetNetIdentity()?.NetId ?? 0;

		internal static void Postfix(int __state) => BionicExplosionSync.Cleanup(__state);
	}
}
