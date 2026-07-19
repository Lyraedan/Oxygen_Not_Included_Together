using System.Collections.Generic;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.DLC.SpacedOut;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.Patches.DLC.SpacedOut
{
	internal static class HighEnergyParticleSync
	{
		internal const int MaxTombstones = 512;
		internal const int MaxPendingStates = 512;
		internal const float TombstoneLifetimeSeconds = 120f;
		internal static readonly PacketSendMode DomainSendMode = PacketSendMode.ReliableImmediate;
		private static readonly Dictionary<int, Tombstone> Tombstones = new();
		private static readonly Dictionary<int, HighEnergyParticleStatePacket> PendingStates = new();
		private static HighEnergyParticlePort _capturePort;
		private static bool _producerCaptureActive;
		private static HighEnergyParticle _producerParticle;
		private static int _applyingNetId;
		private static int _applyingRevision = -1;
		private static bool _retrying;

		private struct Tombstone
		{
			internal int Revision;
			internal float ExpiresAt;
		}

		public static void ResetSessionState()
		{
			Tombstones.Clear();
			PendingStates.Clear();
			_capturePort = null;
			_applyingNetId = 0;
			_applyingRevision = -1;
			_retrying = false;
			CancelProducerCapture();
		}

		internal static int TombstoneCount => Tombstones.Count;
		internal static int PendingCount => PendingStates.Count;
		internal static void CachePending(HighEnergyParticleStatePacket packet)
		{
			if (PendingStates.TryGetValue(packet.NetId, out HighEnergyParticleStatePacket current) &&
			    current.Revision >= packet.Revision)
				return;
			if (!PendingStates.ContainsKey(packet.NetId) && PendingStates.Count >= MaxPendingStates)
			{
				int victim = 0;
				foreach (int netId in PendingStates.Keys) { victim = netId; break; }
				PendingStates.Remove(victim);
			}
			PendingStates[packet.NetId] = packet;
		}

		internal static bool TryGetPendingRevision(int netId, out int revision)
		{
			revision = PendingStates.TryGetValue(netId, out HighEnergyParticleStatePacket packet)
				? packet.Revision : -1;
			return revision >= 0;
		}

		internal static void RecordTombstone(int netId, int revision, float now)
		{
			if (netId == 0 || revision < 0)
				return;
			PruneTombstones(now);
			if (Tombstones.TryGetValue(netId, out Tombstone existing))
				revision = System.Math.Max(revision, existing.Revision);
			else if (Tombstones.Count >= MaxTombstones)
				EvictOldestTombstone();
			Tombstones[netId] = new Tombstone
			{
				Revision = revision,
				ExpiresAt = now + TombstoneLifetimeSeconds
			};
		}

		internal static bool IsTombstoned(int netId, int incomingRevision, float now)
		{
			PruneTombstones(now);
			return Tombstones.TryGetValue(netId, out Tombstone entry) &&
			       !NeedsApply(entry.Revision, incomingRevision);
		}

		internal static bool NeedsApply(int appliedRevision, int incomingRevision)
			=> incomingRevision > appliedRevision;

		internal static bool ShouldRunProducer(bool inSession, bool localIsHost)
			=> !inSession || localIsHost;

		internal static bool ShouldPublishProducerOutcome(bool callerCompleted, int netId)
			=> callerCompleted && netId != 0;

		internal static bool ShouldDestroyUnassignedSpawn(
			bool inSession,
			bool localIsClient,
			bool isApplying,
			int netId)
			=> inSession && localIsClient && !isApplying && netId == 0;

		internal static void BeginCapture(HighEnergyParticlePort port) => _capturePort = port;
		internal static void EndCapture() => _capturePort = null;

		internal static bool BeginProducerCapture()
		{
			if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost)
				return false;
			_producerCaptureActive = true;
			_producerParticle = null;
			return true;
		}

		internal static void RecordProducerSpawn(HighEnergyParticle particle)
		{
			if (_producerCaptureActive && particle != null)
				_producerParticle = particle;
		}

		internal static void CompleteProducerCapture()
		{
			HighEnergyParticle particle = _producerParticle;
			bool callerCompleted = _producerCaptureActive;
			CancelProducerCapture();
			if (particle == null)
				return;

			int netId = particle.GetNetIdentity()?.NetId ?? 0;
			if (!ShouldPublishProducerOutcome(callerCompleted, netId))
			{
				DestroyUnassignedParticle(particle);
				return;
			}

			if (TryCapture(particle, HighEnergyParticle.CollisionType.None,
				    out HighEnergyParticleStatePacket packet))
				Publish(packet);
		}

		internal static void CancelProducerCapture()
		{
			_producerCaptureActive = false;
			_producerParticle = null;
		}

		internal static bool TryCapture(HighEnergyParticle particle,
			HighEnergyParticle.CollisionType collision, out HighEnergyParticleStatePacket packet)
		{
			packet = null;
			int netId = particle?.GetNetIdentity()?.NetId ?? 0;
			if (netId == 0)
				return false;

			HighEnergyParticleStorage storage = _capturePort?.GetComponent<HighEnergyParticleStorage>();
			int revision = particle.gameObject.AddOrGet<HighEnergyParticleSyncMarker>().Revision;
			packet = new HighEnergyParticleStatePacket
			{
				NetId = netId,
				Revision = revision,
				Position = particle.transform.GetPosition(),
				Direction = Traverse.Create(particle).Field("direction").GetValue<EightDirection>(),
				Speed = particle.speed,
				Payload = particle.payload,
				CapturedByNetId = particle.capturedBy?.GetNetIdentity()?.NetId ?? 0,
				Collision = collision,
				CaptureStorageNetId = storage?.GetNetIdentity()?.NetId ?? 0,
				CaptureStoredParticles = storage?.Particles ?? 0f
			};
			return packet.IsWireValid();
		}

		internal static bool Publish(HighEnergyParticleStatePacket packet)
		{
			if (packet == null || !packet.IsWireValid() ||
			    !NetworkIdentityRegistry.TryGet(packet.NetId, out NetworkIdentity identity))
				return false;
			identity.EnsureAuthoritativeSpawnBroadcast();
			PacketSender.SendToAllClients(packet, DomainSendMode);
			return true;
		}

		private static void DestroyUnassignedParticle(HighEnergyParticle particle)
		{
			if (particle == null || particle.gameObject == null)
				return;
			NetworkIdentity identity = particle.GetNetIdentity();
			NetworkIdentityRegistry.UntrackUnassigned(identity);
			Util.KDestroyGameObject(particle.gameObject);
		}

		internal static bool TryApply(HighEnergyParticleStatePacket packet)
		{
			if (packet == null || !packet.IsWireValid())
				return false;
			HighEnergyParticlePort capturedBy = null;
			if (packet.CapturedByNetId != 0 &&
			    !NetworkIdentityRegistry.TryGetComponent(packet.CapturedByNetId, out capturedBy))
			{
				CachePending(packet);
				return false;
			}
			HighEnergyParticleStorage storage = null;
			if (packet.CaptureStorageNetId != 0 &&
			    !NetworkIdentityRegistry.TryGetComponent(packet.CaptureStorageNetId, out storage))
			{
				CachePending(packet);
				return false;
			}

			if (!NetworkIdentityRegistry.TryGetComponent(packet.NetId, out HighEnergyParticle particle) || particle == null)
			{
				if (IsTombstoned(packet.NetId, packet.Revision, Time.unscaledTime))
				{
					PendingStates.Remove(packet.NetId);
					return true;
				}
				CachePending(packet);
				return false;
			}

			HighEnergyParticleSyncMarker marker = particle.gameObject.AddOrGet<HighEnergyParticleSyncMarker>();
			PendingStates.Remove(packet.NetId);
			if (!NeedsApply(marker.AppliedRevision, packet.Revision))
				return true;
			_applyingNetId = packet.NetId;
			_applyingRevision = packet.Revision;
			try
			{
				ApplyState(packet, particle, capturedBy, storage);
			}
			finally
			{
				_applyingNetId = 0;
				_applyingRevision = -1;
			}
			bool particleAlive = !particle.IsNullOrDestroyed() && !particle.gameObject.IsNullOrDestroyed();
			CompleteApply(packet.NetId, particleAlive, !marker.IsNullOrDestroyed(),
				() => marker.AppliedRevision = packet.Revision);
			return true;
		}
		internal static void CompleteApply(int netId, bool particleAlive, bool markerAlive,
			System.Action commitMarker)
		{
			if (!particleAlive || !markerAlive)
				return;
			commitMarker();
			Tombstones.Remove(netId);
		}
		private static void ApplyState(HighEnergyParticleStatePacket packet,
			HighEnergyParticle particle, HighEnergyParticlePort capturedBy,
			HighEnergyParticleStorage storage)
		{
			SpacedOutSyncGuard.Run(() =>
			{
				if (storage != null)
				{
					storage.ConsumeAll();
					storage.Store(packet.CaptureStoredParticles);
				}
				particle.transform.SetPosition(packet.Position);
				particle.speed = packet.Speed;
				particle.payload = packet.Payload;
				particle.capturedBy = capturedBy;
				particle.SetDirection(packet.Direction);
				if (packet.Collision != HighEnergyParticle.CollisionType.None)
					particle.Collide(packet.Collision);
			});
		}
		internal static void RetryPending()
		{
			if (_retrying) return;
			_retrying = true;
			try
			{
				foreach (HighEnergyParticleStatePacket packet in
				         new List<HighEnergyParticleStatePacket>(PendingStates.Values))
					TryApply(packet);
			}
			finally { _retrying = false; }
		}

		internal static void IdentityAvailable()
		{
			if (MultiplayerSession.InSession && MultiplayerSession.IsClient) RetryPending();
		}

		internal static void ScheduleUnassignedCleanup(HighEnergyParticle particle)
		{
			GameScheduler.Instance?.ScheduleNextFrame("ONI Together HEP authority check", _ =>
			{
				int netId = particle?.GetNetIdentity()?.NetId ?? 0;
				if (ShouldDestroyUnassignedSpawn(MultiplayerSession.InSession,
					    MultiplayerSession.IsClient, SpacedOutSyncGuard.IsApplying, netId))
					DestroyUnassignedParticle(particle);
			});
		}

		internal static void RecordCleanup(HighEnergyParticle particle)
		{
			int netId = particle?.GetNetIdentity()?.NetId ?? 0;
			int revision = particle?.GetComponent<HighEnergyParticleSyncMarker>()?.AppliedRevision ?? -1;
			if (netId == _applyingNetId)
				revision = System.Math.Max(revision, _applyingRevision);
			RecordTombstone(netId, revision, Time.unscaledTime);
		}

		private static void PruneTombstones(float now)
		{
			foreach (int netId in new List<int>(Tombstones.Keys))
				if (Tombstones[netId].ExpiresAt < now)
					Tombstones.Remove(netId);
		}

		private static void EvictOldestTombstone()
		{
			int oldestId = 0;
			float oldestExpiry = float.MaxValue;
			foreach (KeyValuePair<int, Tombstone> entry in Tombstones)
				if (entry.Value.ExpiresAt < oldestExpiry)
				{
					oldestId = entry.Key;
					oldestExpiry = entry.Value.ExpiresAt;
				}
			Tombstones.Remove(oldestId);
		}
	}

	internal sealed class HighEnergyParticleSyncMarker : KMonoBehaviour
	{
		internal int Revision;
		internal int AppliedRevision = -1;
	}

	[HarmonyPatch(typeof(NetworkIdentity), nameof(NetworkIdentity.OnSpawn))]
	internal static class HighEnergyParticleIdentitySpawnPatch
	{
		internal static void Postfix() => HighEnergyParticleSync.IdentityAvailable();
	}

	[HarmonyPatch(typeof(NetworkIdentity), nameof(NetworkIdentity.OverrideNetId))]
	internal static class HighEnergyParticleIdentityUpdatePatch
	{
		internal static void Postfix(bool __result)
		{
			if (__result) HighEnergyParticleSync.IdentityAvailable();
		}
	}

	[HarmonyPatch(typeof(HighEnergyParticle), "OnSpawn")]
	internal static class HighEnergyParticleSpawnPatch
	{
		internal static void Postfix(HighEnergyParticle __instance)
		{
			NetworkIdentity identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();
			__instance.gameObject.AddOrGet<EntityPositionHandler>();
			__instance.gameObject.AddOrGet<HighEnergyParticleSyncMarker>();
			if (HighEnergyParticleSync.ShouldDestroyUnassignedSpawn(
				    MultiplayerSession.InSession,
				    MultiplayerSession.IsClient,
				    SpacedOutSyncGuard.IsApplying,
				    identity.NetId))
				HighEnergyParticleSync.ScheduleUnassignedCleanup(__instance);
			HighEnergyParticleSync.RecordProducerSpawn(__instance);
		}
	}

	[HarmonyPatch(typeof(HighEnergyParticle), "OnCleanUp")]
	internal static class HighEnergyParticleCleanupPatch
	{
		internal static void Prefix(HighEnergyParticle __instance)
		{
			if (MultiplayerSession.InSession && MultiplayerSession.IsClient)
				HighEnergyParticleSync.RecordCleanup(__instance);
		}
	}

	internal static class HighEnergyParticleProducerPatch
	{
		internal static bool Prefix(out bool captureOutcome)
		{
			bool run = HighEnergyParticleSync.ShouldRunProducer(
				MultiplayerSession.InSession, MultiplayerSession.IsHost);
			captureOutcome = run && HighEnergyParticleSync.BeginProducerCapture();
			return run;
		}

		internal static void Postfix(bool captureOutcome)
		{
			if (captureOutcome)
				HighEnergyParticleSync.CompleteProducerCapture();
		}

		internal static System.Exception Finalizer(
			System.Exception exception,
			bool captureOutcome)
		{
			if (captureOutcome && exception != null)
				HighEnergyParticleSync.CancelProducerCapture();
			return exception;
		}
	}

	[HarmonyPatch(typeof(HighEnergyParticleSpawner), nameof(HighEnergyParticleSpawner.LauncherUpdate), typeof(float))]
	internal static class HighEnergyParticleSpawnerAuthorityPatch
	{
		internal static bool Prefix(out bool __state)
			=> HighEnergyParticleProducerPatch.Prefix(out __state);

		internal static void Postfix(bool __state)
			=> HighEnergyParticleProducerPatch.Postfix(__state);

		internal static System.Exception Finalizer(System.Exception __exception, bool __state)
			=> HighEnergyParticleProducerPatch.Finalizer(__exception, __state);
	}

	[HarmonyPatch(typeof(ManualHighEnergyParticleSpawner), nameof(ManualHighEnergyParticleSpawner.LauncherUpdate))]
	internal static class ManualHighEnergyParticleSpawnerAuthorityPatch
	{
		internal static bool Prefix(out bool __state)
			=> HighEnergyParticleProducerPatch.Prefix(out __state);

		internal static void Postfix(bool __state)
			=> HighEnergyParticleProducerPatch.Postfix(__state);

		internal static System.Exception Finalizer(System.Exception __exception, bool __state)
			=> HighEnergyParticleProducerPatch.Finalizer(__exception, __state);
	}

	[HarmonyPatch(typeof(HighEnergyParticleRedirector), "LaunchParticle")]
	internal static class HighEnergyParticleRedirectorAuthorityPatch
	{
		internal static bool Prefix(out bool __state)
			=> HighEnergyParticleProducerPatch.Prefix(out __state);

		internal static void Postfix(bool __state)
			=> HighEnergyParticleProducerPatch.Postfix(__state);

		internal static System.Exception Finalizer(System.Exception __exception, bool __state)
			=> HighEnergyParticleProducerPatch.Finalizer(__exception, __state);
	}

	[HarmonyPatch(typeof(HighEnergyParticle), "Capture")]
	internal static class HighEnergyParticleCapturePatch
	{
		internal static void Prefix(HighEnergyParticlePort input)
		{
			if (MultiplayerSession.InSession && MultiplayerSession.IsHost)
				HighEnergyParticleSync.BeginCapture(input);
		}

		internal static void Postfix() => HighEnergyParticleSync.EndCapture();
		internal static System.Exception Finalizer(System.Exception __exception)
		{
			HighEnergyParticleSync.EndCapture();
			return __exception;
		}
	}

	[HarmonyPatch(typeof(HighEnergyParticle), nameof(HighEnergyParticle.MovingUpdate))]
	internal static class HighEnergyParticleMovingPatch
	{
		internal static bool Prefix()
			=> SpacedOutSyncGuard.IsApplying || !MultiplayerSession.InSession || !MultiplayerSession.IsClient;
	}

	[HarmonyPatch(typeof(HighEnergyParticle), nameof(HighEnergyParticle.CheckCollision))]
	internal static class HighEnergyParticleCollisionCheckPatch
	{
		internal static bool Prefix()
			=> SpacedOutSyncGuard.IsApplying || !MultiplayerSession.InSession || !MultiplayerSession.IsClient;
	}

	[HarmonyPatch(typeof(HighEnergyParticle), nameof(HighEnergyParticle.Collide))]
	internal static class HighEnergyParticleCollisionPatch
	{
		internal static bool Prefix(HighEnergyParticle __instance,
			HighEnergyParticle.CollisionType collisionType, ref HighEnergyParticleStatePacket __state)
		{
			if (MultiplayerSession.InSession && MultiplayerSession.IsClient && !SpacedOutSyncGuard.IsApplying)
				return false;
			if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost || SpacedOutSyncGuard.IsApplying)
				return true;

			HighEnergyParticleSyncMarker marker = __instance.gameObject.AddOrGet<HighEnergyParticleSyncMarker>();
			marker.Revision++;
			HighEnergyParticleSync.TryCapture(__instance, collisionType, out __state);
			return true;
		}

		internal static void Postfix(HighEnergyParticleStatePacket __state)
		{
			if (__state != null)
				HighEnergyParticleSync.Publish(__state);
		}
	}
}
