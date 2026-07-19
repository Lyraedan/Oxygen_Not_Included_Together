using System.Collections.Generic;
using System.Linq;
using ONI_Together.Networking.Components;
using UnityEngine;

namespace ONI_Together.Networking.Packets.World
{
	public partial class SpawnPrefabPacket
	{
		internal const int MaxPendingBindings = 2048;
		internal const float PendingBindingLifetimeSeconds = 120f;
		private static readonly Dictionary<(int NetId, ulong Lifecycle), PendingBinding>
			PendingBindings = new();

		private readonly struct PendingBinding
		{
			internal readonly SpawnPrefabPacket Packet;
			internal readonly float ExpiresAt;

			internal PendingBinding(SpawnPrefabPacket packet, float expiresAt)
			{
				Packet = packet;
				ExpiresAt = expiresAt;
			}
		}

		private static void StorePendingBinding(SpawnPrefabPacket packet)
			=> StorePendingBinding(packet, Time.realtimeSinceStartup);

		private static void StorePendingBinding(SpawnPrefabPacket packet, float now)
		{
			if (packet == null || packet.NetId == 0 || packet.Revision == 0)
				return;
			PrunePendingBindings(now);
			var key = (packet.NetId, packet.Revision);
			ulong newest = PendingBindings.Keys
				.Where(value => value.NetId == packet.NetId)
				.Select(value => value.Lifecycle).DefaultIfEmpty(0UL).Max();
			if (newest > packet.Revision)
				return;
			foreach (var stale in PendingBindings.Keys.Where(value =>
				         value.NetId == packet.NetId && value.Lifecycle < packet.Revision).ToArray())
				PendingBindings.Remove(stale);
			if (!PendingBindings.ContainsKey(key) && PendingBindings.Count >= MaxPendingBindings)
				PendingBindings.Remove(PendingBindings.OrderBy(value => value.Value.ExpiresAt).First().Key);
			PendingBindings[key] = new PendingBinding(
				packet, now + PendingBindingLifetimeSeconds);
		}

		internal static void TryApplyPendingBindings()
		{
			PrunePendingBindings(Time.realtimeSinceStartup);
			foreach (var entry in PendingBindings.ToArray())
			{
				SpawnPrefabPacket packet = entry.Value.Packet;
				ulong current = NetworkIdentityRegistry.GetLastLifecycleRevision(packet.NetId);
				bool tombstoned = NetworkIdentityRegistry.IsLifecycleTombstoned(packet.NetId);
				if (current > packet.Revision || current == packet.Revision && tombstoned)
				{
					PendingBindings.Remove(entry.Key);
					continue;
				}
				if (TryFinishOccupied(packet) || TryClaimAndFinish(packet))
					PendingBindings.Remove(entry.Key);
			}
		}

		private static bool TryFinishOccupied(SpawnPrefabPacket packet)
		{
			NetworkIdentityRegistry.ReleaseUnavailableRegistration(packet.NetId);
			if (!NetworkIdentityRegistry.TryGet(packet.NetId, out NetworkIdentity occupied)
			    || occupied.gameObject.PrefabID().GetHashCode() != packet.Hash
			    || !NetworkIdentityRegistry.TryBeginRegisteredMutation(
				    occupied, packet.NetId,
				    out NetworkIdentityRegistry.IdentityClaim mutation))
				return false;
			if (packet.FinishRuntimeMaterialization(occupied.gameObject))
				return true;
			NetworkIdentityRegistry.RollbackClaim(mutation);
			return false;
		}

		private static bool TryClaimAndFinish(SpawnPrefabPacket packet)
		{
			if (NetworkIdentityRegistry.Exists(packet.NetId))
				return false;
			NetworkIdentityRegistry.IdentityClaim claim;
			bool claimed = packet.BindExistingOnly
				? NetworkIdentityRegistry.TryBeginAuthorityBindingClaim(
					packet.Hash, packet.Position, packet.WorldId, packet.NetId, out claim)
				: NetworkIdentityRegistry.TryBeginUnassignedClaim(
					packet.Hash, packet.Position, packet.WorldId, packet.NetId, out claim);
			if (!claimed)
				return false;
			if (packet.FinishRuntimeMaterialization(claim.GameObject))
				return true;
			NetworkIdentityRegistry.RollbackClaim(claim);
			return false;
		}

		private static void PrunePendingBindings(float now)
		{
			foreach (var entry in PendingBindings.Where(
				         value => value.Value.ExpiresAt <= now).ToArray())
				PendingBindings.Remove(entry.Key);
		}

		internal static void ClearPendingBindings() => PendingBindings.Clear();

		internal static void CancelPendingBinding(int netId)
		{
			foreach (var key in PendingBindings.Keys.Where(key => key.NetId == netId).ToArray())
				PendingBindings.Remove(key);
		}

		internal static int PendingBindingCountForTests => PendingBindings.Count;
		internal static bool HasPendingBindingForTests(int netId, ulong lifecycle)
			=> PendingBindings.ContainsKey((netId, lifecycle));
		internal static void StorePendingBindingForTests(SpawnPrefabPacket packet, float now)
			=> StorePendingBinding(packet, now);
		internal static void PrunePendingBindingsForTests(float now)
			=> PrunePendingBindings(now);
	}
}
