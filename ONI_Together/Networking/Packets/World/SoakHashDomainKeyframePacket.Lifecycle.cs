#if DEBUG
using System.Collections.Generic;
using System.Linq;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;

namespace ONI_Together.Networking.Packets.World
{
	public sealed partial class SoakHashDomainKeyframePacket
	{
		private static bool TryReconcileLifecycle(
			IReadOnlyList<SoakHashDomainKeyframePacket> packets,
			IReadOnlyList<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry> baseline)
		{
			if (!TryValidateLifecycleSnapshotSet(
				    packets, baseline, out HashSet<int> liveNetIds))
				return false;
			if (!NetworkIdentityRegistry.TryReplaceLifecycleRevisionBaseline(baseline))
			{
				DebugConsole.LogWarning("[SoakKeyframe] lifecycle journal replacement failed");
				return false;
			}
			foreach (SoakHashDomainKeyframePacket packet in packets)
			{
				if (!packet.LifecycleSnapshot.TryApplySnapshot())
				{
					LogDescriptorFailure(
						packet,
						$"snapshot apply failed: " +
						$"{SpawnPrefabPacket.GetSnapshotDiagnostic(packet.NetId)}");
					return false;
				}
			}
			NetworkIdentityRegistry.RemoveUnexpectedLifecycleObjects(liveNetIds);
			if (!NetworkIdentityRegistry.TryReplaceLifecycleRevisionBaseline(baseline))
			{
				DebugConsole.LogWarning("[SoakKeyframe] final lifecycle journal replacement failed");
				return false;
			}
			NetworkIdentityRegistry.LifecycleMembershipValidationResult membership =
				NetworkIdentityRegistry.ValidateCurrentLifecycleMembership(baseline);
			if (!membership.IsValid)
				DebugConsole.LogWarning(
					$"[SoakKeyframe] reconciliation membership mismatch " +
					$"missing={membership.MissingLiveCount} " +
					$"unexpected={membership.UnexpectedLiveCount} " +
					$"tombstoned={membership.TombstonedLiveCount} " +
					$"unassigned={membership.UnassignedLiveCount}");
			if (!membership.IsValid)
				return false;
			NetworkIdentityRegistry.ClearPendingSnapshotDeltas();
			return true;
		}

		private static bool TryValidateLifecycleSnapshotSet(
			IReadOnlyList<SoakHashDomainKeyframePacket> packets,
			IReadOnlyList<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry> baseline,
			out HashSet<int> liveNetIds)
		{
			liveNetIds = new HashSet<int>();
			if (packets == null || baseline == null)
				return false;
			var revisions = new Dictionary<int, ulong>();
			foreach (NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry entry in baseline)
			{
				if (entry.NetId == 0 || entry.Revision == 0
				    || !revisions.TryAdd(entry.NetId, entry.Revision))
					return false;
				if (!entry.Tombstoned)
					liveNetIds.Add(entry.NetId);
			}
			return ValidateLiveDescriptors(packets, liveNetIds, revisions);
		}

		private static bool ValidateLiveDescriptors(
			IReadOnlyList<SoakHashDomainKeyframePacket> packets,
			HashSet<int> liveNetIds, IReadOnlyDictionary<int, ulong> revisions)
		{
			var packetIds = new HashSet<int>();
			foreach (SoakHashDomainKeyframePacket packet in packets)
			{
				if (!packetIds.Add(packet.NetId) || !liveNetIds.Contains(packet.NetId)
				    || !revisions.TryGetValue(packet.NetId, out ulong revision)
				    || packet.LifecycleSnapshot.Revision != revision)
				{
					LogDescriptorFailure(packet, "lifecycle descriptor set mismatch");
					return false;
				}
				string failure = packet.LifecycleSnapshot.GetSnapshotApplicabilityFailure();
				if (failure != null)
				{
					LogDescriptorFailure(packet, failure);
					return false;
				}
			}
			return packetIds.SetEquals(liveNetIds);
		}

		private static void LogDescriptorFailure(
			SoakHashDomainKeyframePacket packet, string reason)
		{
			SpawnPrefabPacket descriptor = packet?.LifecycleSnapshot;
			DebugConsole.LogWarning(
				$"[SoakKeyframe] preflight rejected NetId {packet?.NetId ?? 0}: {reason}; " +
				$"hash={descriptor?.Hash ?? 0}, world={descriptor?.WorldId ?? -1}, " +
				$"bindExisting={descriptor?.BindExistingOnly ?? false}.");
		}

	}
}
#endif
