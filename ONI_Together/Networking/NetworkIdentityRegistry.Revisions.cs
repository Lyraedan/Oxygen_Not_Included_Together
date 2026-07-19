using System;
using System.Collections.Generic;
using System.Linq;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using UnityEngine;

namespace ONI_Together.Networking
{
	public static partial class NetworkIdentityRegistry
	{
		internal readonly struct LifecycleRevisionState
		{
			internal readonly bool HasRevision;
			internal readonly ulong Revision;
			internal readonly bool Tombstoned;

			internal LifecycleRevisionState(bool hasRevision, ulong revision, bool tombstoned)
			{
				HasRevision = hasRevision;
				Revision = revision;
				Tombstoned = tombstoned;
			}
		}

		public static ulong NextAuthorityRevision()
		{
			nextAuthorityRevision++;
			if (nextAuthorityRevision == 0)
				nextAuthorityRevision = 1;
			return nextAuthorityRevision;
		}

		public static ulong BeginLifecycle(int netId)
		{
			if (netId == 0)
				return 0;
			if (lifecycleRevisions.TryGetValue(netId, out ulong revision)
			    && !lifecycleTombstones.Contains(netId))
				return revision;
			revision = NextAuthorityRevision();
			lifecycleRevisions[netId] = revision;
			lifecycleTombstones.Remove(netId);
			return revision;
		}

		public static ulong EndLifecycle(int netId)
		{
			if (netId == 0)
				return 0;
			if (lifecycleTombstones.Contains(netId)
			    && lifecycleRevisions.TryGetValue(netId, out ulong existing))
				return existing;
			ulong revision = NextAuthorityRevision();
			lifecycleRevisions[netId] = revision;
			lifecycleTombstones.Add(netId);
			return revision;
		}

		public static bool TryAcceptLifecycleRevision(int netId, ulong revision, bool tombstone)
		{
			if (!IsNewerRevision(GetLastLifecycleRevision(netId), revision))
				return false;
			lifecycleRevisions[netId] = revision;
			if (tombstone)
				lifecycleTombstones.Add(netId);
			else
				lifecycleTombstones.Remove(netId);
			return true;
		}

		public static bool TryBindAuthoritativeLifecycle(
			GameObject gameObject, int netId, ulong revision)
		{
			if (gameObject == null || netId == 0 || revision == 0)
				return false;
			if (MultiplayerSession.IsClient)
				ReleaseUnavailableRegistration(netId);
			ulong current = GetLastLifecycleRevision(netId);
			if (current > revision || current == revision && IsLifecycleTombstoned(netId))
				return false;
			NetworkIdentity identity = gameObject.AddOrGet<NetworkIdentity>();
			if (!identity.OverrideNetId(netId))
				return false;
			bool accepted = current == revision
			                || TryAcceptLifecycleRevision(netId, revision, tombstone: false);
			if (accepted)
				identity.LifecycleRevision = revision;
			return accepted;
		}

		public static ulong GetLastLifecycleRevision(int netId)
			=> lifecycleRevisions.TryGetValue(netId, out ulong revision) ? revision : 0;

		public static bool IsLifecycleTombstoned(int netId) => lifecycleTombstones.Contains(netId);

		internal static LifecycleRevisionState CaptureLifecycleRevisionState(int netId)
		{
			bool hasRevision = lifecycleRevisions.TryGetValue(netId, out ulong revision);
			return new LifecycleRevisionState(
				hasRevision, revision, lifecycleTombstones.Contains(netId));
		}

		internal static void RestoreLifecycleRevisionState(
			int netId, LifecycleRevisionState state)
		{
			if (state.HasRevision)
				lifecycleRevisions[netId] = state.Revision;
			else
				lifecycleRevisions.Remove(netId);
			if (state.Tombstoned)
				lifecycleTombstones.Add(netId);
			else
				lifecycleTombstones.Remove(netId);
		}

		public static IReadOnlyList<LifecycleRevisionSnapshotEntry> GetLifecycleRevisionSnapshot()
		{
			return lifecycleRevisions
				.Select(entry => CreateLifecycleSnapshotEntry(entry.Key, entry.Value))
				.OrderBy(entry => entry.NetId)
				.ToArray();
		}

		private static LifecycleRevisionSnapshotEntry CreateLifecycleSnapshotEntry(
			int netId, ulong revision)
		{
			bool tombstoned = lifecycleTombstones.Contains(netId);
			SpawnPrefabPacket descriptor = null;
			if (!tombstoned && identities.TryGetValue(netId, out NetworkIdentity identity)
			    && !identity.IsNullOrDestroyed() && !identity.gameObject.IsNullOrDestroyed())
			{
				descriptor = SpawnPrefabPacket.FromIdentity(
					identity, requireExistingPersistentObject: true);
				if (descriptor != null)
					descriptor.Revision = revision;
			}
			return new LifecycleRevisionSnapshotEntry(
				netId, revision, tombstoned, descriptor);
		}

		public static bool TryReplaceLifecycleRevisionBaseline(
			IReadOnlyList<LifecycleRevisionSnapshotEntry> baseline)
		{
			if (baseline == null)
				return false;
			var revisions = new Dictionary<int, ulong>(baseline.Count);
			var tombstones = new HashSet<int>();
			ulong maximumRevision = 0;
			foreach (LifecycleRevisionSnapshotEntry entry in baseline)
			{
				if (entry.NetId == 0 || entry.Revision == 0
				    || !revisions.TryAdd(entry.NetId, entry.Revision))
					return false;
				if (entry.Tombstoned)
					tombstones.Add(entry.NetId);
				maximumRevision = Math.Max(maximumRevision, entry.Revision);
			}
			lifecycleRevisions.Clear();
			foreach (var entry in revisions)
				lifecycleRevisions.Add(entry.Key, entry.Value);
			lifecycleTombstones.Clear();
			lifecycleTombstones.UnionWith(tombstones);
			nextAuthorityRevision = Math.Max(nextAuthorityRevision, maximumRevision);
			return true;
		}

		internal static ulong AuthorityRevisionForTests => nextAuthorityRevision;

		internal static bool RestoreLifecycleRevisionStateForTests(
			IReadOnlyList<LifecycleRevisionSnapshotEntry> baseline,
			ulong authorityRevision)
			=> RestoreLifecycleRevisionState(baseline, authorityRevision);

		private static bool RestoreLifecycleRevisionState(
			IReadOnlyList<LifecycleRevisionSnapshotEntry> baseline,
			ulong authorityRevision)
		{
			if (!TryReplaceLifecycleRevisionBaseline(baseline))
				return false;
			nextAuthorityRevision = authorityRevision;
			return true;
		}

		internal static LifecycleMembershipValidationResult ValidateLifecycleMembership(
			IReadOnlyList<LifecycleRevisionSnapshotEntry> baseline,
			IEnumerable<int> liveNetIds)
		{
			if (baseline == null || liveNetIds == null)
				return new LifecycleMembershipValidationResult(false, 0, 0, 0);
			var all = new HashSet<int>();
			var expectedLive = new HashSet<int>();
			var tombstoned = new HashSet<int>();
			foreach (LifecycleRevisionSnapshotEntry entry in baseline)
			{
				if (entry.NetId == 0 || entry.Revision == 0 || !all.Add(entry.NetId))
					return new LifecycleMembershipValidationResult(false, 0, 0, 0);
				(entry.Tombstoned ? tombstoned : expectedLive).Add(entry.NetId);
			}
			var live = new HashSet<int>(liveNetIds);
			int missing = expectedLive.Count(netId => !live.Contains(netId));
			int unexpected = live.Count(netId => !all.Contains(netId));
			int tombstonedLive = live.Count(tombstoned.Contains);
			return new LifecycleMembershipValidationResult(
				true, missing, unexpected, tombstonedLive);
		}

		internal static LifecycleMembershipValidationResult ValidateCurrentLifecycleMembership(
			IReadOnlyList<LifecycleRevisionSnapshotEntry> baseline)
		{
			int[] liveNetIds = identities
				.Where(entry => !entry.Value.IsNullOrDestroyed() &&
				                !entry.Value.gameObject.IsNullOrDestroyed())
				.Select(entry => entry.Key)
				.ToArray();
			LifecycleMembershipValidationResult result =
				ValidateLifecycleMembership(baseline, liveNetIds);
			int unassignedLive = unassigned.Keys.Count(identity =>
				!identity.IsNullOrDestroyed() && !identity.gameObject.IsNullOrDestroyed());
			return new LifecycleMembershipValidationResult(
				result.BaselineValid, result.MissingLiveCount,
				result.UnexpectedLiveCount, result.TombstonedLiveCount, unassignedLive);
		}

		public static bool TryAcceptStorageSnapshotRevision(int storageNetId, ulong revision)
		{
			if (!ShouldAcceptStorageSnapshotRevision(storageNetId, revision))
				return false;
			storageSnapshotRevisions[storageNetId] = revision;
			return true;
		}

		internal static bool ShouldAcceptStorageSnapshotRevision(
			int storageNetId, ulong revision)
		{
			if (storageNetId == 0)
				return false;
			ulong baseline = Math.Max(
				Math.Max(GetLastStorageSnapshotRevision(storageNetId),
					GetLastStorageEventRevision(storageNetId)),
				GetLastLifecycleRevision(storageNetId));
			return IsNewerRevision(baseline, revision);
		}

		internal static bool TryCommitStorageSnapshotRevisions(
			IReadOnlyDictionary<int, ulong> revisions)
		{
			if (revisions == null)
				return false;
			foreach (var revision in revisions)
				if (!ShouldAcceptStorageSnapshotRevision(revision.Key, revision.Value))
					return false;
			foreach (var revision in revisions)
				storageSnapshotRevisions[revision.Key] = revision.Value;
			return true;
		}

		public static bool TryAcceptStateAndStorageSnapshotRevision(
			int netId, string domain, ulong revision)
		{
			var key = (netId, domain ?? string.Empty);
			ulong stateCurrent = stateRevisions.TryGetValue(key, out ulong value) ? value : 0;
			ulong snapshotCurrent = GetLastStorageSnapshotRevision(netId);
			ulong eventCurrent = GetLastStorageEventRevision(netId);
			ulong lifecycleCurrent = GetLastLifecycleRevision(netId);
			if (!ShouldAcceptStateAndStorageSnapshotRevision(
				    stateCurrent, snapshotCurrent, eventCurrent, lifecycleCurrent, revision))
				return false;
			stateRevisions[key] = revision;
			storageSnapshotRevisions[netId] = revision;
			return true;
		}

		internal static bool ShouldAcceptStateAndStorageSnapshotRevision(
			ulong stateCurrent,
			ulong snapshotCurrent,
			ulong eventCurrent,
			ulong lifecycleCurrent,
			ulong incoming)
		{
			ulong storageCurrent = Math.Max(
				Math.Max(snapshotCurrent, eventCurrent), lifecycleCurrent);
			return IsNewerRevision(stateCurrent, incoming)
			       && IsNewerRevision(storageCurrent, incoming);
		}

		public static bool TryAcceptStorageTransferRevision(
			int storageNetId, int itemNetId, ulong revision)
		{
			if (!StorageItemPacket.ShouldApplyRevision(
				    GetLastStorageSnapshotRevision(storageNetId),
				    GetLastStorageItemRevision(itemNetId),
				    GetLastLifecycleRevision(itemNetId),
				    GetLastLifecycleRevision(storageNetId),
				    revision))
				return false;
			storageEventRevisions[storageNetId] =
				Math.Max(GetLastStorageEventRevision(storageNetId), revision);
			storageItemRevisions[itemNetId] = revision;
			return true;
		}

		public static ulong GetLastStorageSnapshotRevision(int storageNetId)
			=> storageSnapshotRevisions.TryGetValue(storageNetId, out ulong revision) ? revision : 0;

		public static ulong GetLastStorageEventRevision(int storageNetId)
			=> storageEventRevisions.TryGetValue(storageNetId, out ulong revision) ? revision : 0;

		public static ulong GetLastStorageItemRevision(int itemNetId)
			=> storageItemRevisions.TryGetValue(itemNetId, out ulong revision) ? revision : 0;

		public static bool IsCurrentStorageTransferRevision(
			int storageNetId, int itemNetId, ulong revision)
		{
			return GetLastStorageItemRevision(itemNetId) == revision
			       && revision > GetLastStorageSnapshotRevision(storageNetId)
			       && revision > GetLastLifecycleRevision(itemNetId)
			       && revision > GetLastLifecycleRevision(storageNetId);
		}

		public static bool TryAcceptStateRevision(int netId, string domain, ulong revision)
		{
			var key = (netId, domain ?? string.Empty);
			ulong current = stateRevisions.TryGetValue(key, out ulong value) ? value : 0;
			if (!IsNewerRevision(current, revision))
				return false;
			stateRevisions[key] = revision;
			return true;
		}

		public static ulong GetLastStateRevision(int netId, string domain)
			=> stateRevisions.TryGetValue((netId, domain ?? string.Empty), out ulong revision)
				? revision
				: 0;

		public static bool IsCurrentStateRevision(int netId, string domain, ulong revision)
			=> stateRevisions.TryGetValue((netId, domain ?? string.Empty), out ulong current)
			   && current == revision;

		public static bool IsNewerRevision(ulong current, ulong incoming)
			=> incoming != 0 && incoming > current;
	}
}
