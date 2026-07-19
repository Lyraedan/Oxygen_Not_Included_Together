using System.Collections.Generic;
using System.IO;
using System.Linq;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Synchronization;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.Networking.Packets.World
{
	internal sealed class DuplicantDeathStatePacket : IPacket, IHostOnlyPacket
	{
		private const string RevisionDomain = "duplicant-death";
		internal const int MaxPendingDeaths = 1024;
		internal const float PendingLifetimeSeconds = 120f;
		private static readonly Dictionary<(int NetId, ulong Lifecycle), PendingDeath> Pending = new();

		private readonly struct PendingDeath
		{
			internal readonly DuplicantDeathStatePacket Packet;
			internal readonly float ExpiresAt;

			internal PendingDeath(DuplicantDeathStatePacket packet, float expiresAt)
			{
				Packet = packet;
				ExpiresAt = expiresAt;
			}
		}

		public int NetId;
		public ulong LifecycleRevision;
		public ulong Revision;
		public string DeathId = string.Empty;

		internal static bool TryCreate(
			DeathMonitor.Instance monitor, out DuplicantDeathStatePacket packet)
		{
			packet = null;
			if (monitor?.gameObject == null
			    || !monitor.gameObject.TryGetComponent(out NetworkIdentity identity)
			    || identity.NetId == 0
			    || identity.IsLifecycleTerminal
			    || identity.LifecycleRevision == 0
			    || !NetworkIdentityRegistry.IsRegistered(identity, identity.NetId)
			    || NetworkIdentityRegistry.GetLastLifecycleRevision(identity.NetId)
			       != identity.LifecycleRevision
			    || NetworkIdentityRegistry.IsLifecycleTombstoned(identity.NetId)
			    || !DuplicantDeathSync.TryCapture(monitor.gameObject, out bool isDead, out string deathId)
			    || !isDead)
				return false;
			packet = new DuplicantDeathStatePacket
			{
				NetId = identity.NetId,
				LifecycleRevision = identity.LifecycleRevision,
				Revision = NetworkIdentityRegistry.NextAuthorityRevision(),
				DeathId = deathId
			};
			return true;
		}

		public void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(NetId);
			writer.Write(LifecycleRevision);
			writer.Write(Revision);
			DuplicantDeathWire.WriteDeathId(writer, DeathId);
		}

		public void Deserialize(BinaryReader reader)
		{
			NetId = reader.ReadInt32();
			LifecycleRevision = reader.ReadUInt64();
			Revision = reader.ReadUInt64();
			DeathId = DuplicantDeathWire.ReadDeathId(reader);
			Validate();
		}

		public void OnDispatched()
		{
			if (!ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				return;
			ulong current = NetworkIdentityRegistry.GetLastLifecycleRevision(NetId);
			bool tombstoned = NetworkIdentityRegistry.IsLifecycleTombstoned(NetId);
			if (IsStaleLifecycle(current, tombstoned, LifecycleRevision))
				return;
			if (!IsCurrentLifecycle(current, tombstoned, LifecycleRevision)
			    || !NetworkIdentityRegistry.TryGet(NetId, out NetworkIdentity identity)
			    || !OwnsTargetLifecycle(identity))
			{
				StorePending(this, Time.realtimeSinceStartup);
				return;
			}
			TryApply(identity);
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;

		internal static void TryApplyPending(int netId, UnityEngine.GameObject gameObject)
		{
			PruneExpired(Time.realtimeSinceStartup);
			NetworkIdentity identity = gameObject?.GetComponent<NetworkIdentity>();
			if (identity == null)
				return;
			foreach (var entry in Pending.Where(value => value.Key.NetId == netId).ToArray())
			{
				DuplicantDeathStatePacket packet = entry.Value.Packet;
				ulong current = NetworkIdentityRegistry.GetLastLifecycleRevision(netId);
				bool tombstoned = NetworkIdentityRegistry.IsLifecycleTombstoned(netId);
				if (IsStaleLifecycle(current, tombstoned, packet.LifecycleRevision))
				{
					Pending.Remove(entry.Key);
					continue;
				}
				if (!IsCurrentLifecycle(current, tombstoned, packet.LifecycleRevision)
				    || !packet.OwnsTargetLifecycle(identity))
					continue;
				if (!NetworkIdentityRegistry.IsNewerRevision(
					    NetworkIdentityRegistry.GetLastStateRevision(netId, RevisionDomain),
					    packet.Revision)
				    || packet.TryApply(identity))
					Pending.Remove(entry.Key);
			}
		}

		internal static void CancelPending(int netId)
		{
			foreach (var key in Pending.Keys.Where(key => key.NetId == netId).ToArray())
				Pending.Remove(key);
		}

		internal static void ClearPending() => Pending.Clear();
		internal static void ClearState() => ClearPending();

		private bool TryApply(NetworkIdentity identity)
		{
			if (!OwnsTargetLifecycle(identity))
				return false;
			if (!NetworkIdentityRegistry.IsNewerRevision(
				    NetworkIdentityRegistry.GetLastStateRevision(NetId, RevisionDomain), Revision))
				return false;
			if (DuplicantDeathSync.Apply(identity.gameObject, true, DeathId)
			    && NetworkIdentityRegistry.TryAcceptStateRevision(NetId, RevisionDomain, Revision))
				return true;
			DebugConsole.LogWarning($"[DuplicantDeathStatePacket] Failed to apply death for NetId {NetId}");
			return false;
		}

		private bool OwnsTargetLifecycle(NetworkIdentity identity)
		{
			return identity != null
			       && identity.NetId == NetId
			       && identity.LifecycleRevision == LifecycleRevision
			       && NetworkIdentityRegistry.IsRegistered(identity, NetId)
			       && IsCurrentLifecycle(
				       NetworkIdentityRegistry.GetLastLifecycleRevision(NetId),
				       NetworkIdentityRegistry.IsLifecycleTombstoned(NetId),
				       LifecycleRevision);
		}

		private static void StorePending(DuplicantDeathStatePacket packet, float now)
		{
			PruneExpired(now);
			var key = (packet.NetId, packet.LifecycleRevision);
			ulong newerLifecycle = Pending.Keys
				.Where(value => value.NetId == packet.NetId)
				.Select(value => value.Lifecycle)
				.DefaultIfEmpty(0UL)
				.Max();
			if (newerLifecycle > packet.LifecycleRevision)
				return;
			foreach (var stale in Pending.Keys.Where(value =>
				         value.NetId == packet.NetId
				         && value.Lifecycle < packet.LifecycleRevision).ToArray())
				Pending.Remove(stale);
			if (Pending.TryGetValue(key, out PendingDeath current)
			    && current.Packet.Revision >= packet.Revision)
				return;
			if (!Pending.ContainsKey(key) && Pending.Count >= MaxPendingDeaths)
				Pending.Remove(Pending.OrderBy(value => value.Value.ExpiresAt).First().Key);
			Pending[key] = new PendingDeath(packet, now + PendingLifetimeSeconds);
		}

		private static void PruneExpired(float now)
		{
			foreach (var entry in Pending.Where(value => value.Value.ExpiresAt <= now).ToArray())
				Pending.Remove(entry.Key);
		}

		internal static bool IsCurrentLifecycle(
			ulong current, bool tombstoned, ulong incoming)
			=> incoming != 0 && current == incoming && !tombstoned;

		internal static bool IsStaleLifecycle(
			ulong current, bool tombstoned, ulong incoming)
			=> current > incoming || current == incoming && tombstoned;

		internal static int PendingCountForTests => Pending.Count;
		internal static bool HasPendingForTests(int netId, ulong lifecycle)
			=> Pending.ContainsKey((netId, lifecycle));
		internal static void StorePendingForTests(DuplicantDeathStatePacket packet, float now)
			=> StorePending(packet, now);
		internal static void PrunePendingForTests(float now) => PruneExpired(now);

		private void Validate()
		{
			if (NetId == 0 || LifecycleRevision == 0 || Revision == 0
			    || !DuplicantDeathSync.IsValidDeathId(DeathId))
				throw new InvalidDataException("Invalid duplicant death state");
		}
	}
}
