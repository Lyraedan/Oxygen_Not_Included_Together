using System.Collections.Generic;
using System.Linq;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using UnityEngine;

namespace ONI_Together.Networking
{
	public static partial class NetworkIdentityRegistry
	{
		internal static bool TryReconcileLifecycleBaseline(
			IReadOnlyList<LifecycleRevisionSnapshotEntry> baseline,
			out LifecycleMembershipValidationResult membership)
		{
			membership = new LifecycleMembershipValidationResult(false, 0, 0, 0);
			if (!CanReconcileLifecycleBaseline(baseline))
				return false;
			if (!TryReplaceLifecycleRevisionBaseline(baseline))
				return false;
			if (!TryApplyExpectedLifecycle(baseline))
				return false;
			RemoveUnexpectedLifecycleObjects(baseline);
			membership = ValidateCurrentLifecycleMembership(baseline);
			if (!membership.IsValid)
				return false;
			ClearPendingSnapshotDeltas();
			return true;
		}

		internal static void ClearPendingSnapshotDeltas()
		{
			GroundItemPickedUpPacket.ClearPending();
			StorageItemPacket.ClearPending();
			SpawnPrefabPacket.ClearPendingBindings();
			DuplicantDeathStatePacket.ClearPending();
		}

		internal static bool CanReconcileLifecycleBaseline(
			IReadOnlyList<LifecycleRevisionSnapshotEntry> baseline)
		{
			if (baseline == null)
			{
				DebugConsole.LogError("[LifecycleBaseline] Preflight rejected a null baseline.");
				return false;
			}
			var netIds = new HashSet<int>();
			foreach (LifecycleRevisionSnapshotEntry entry in baseline)
			{
				if (!netIds.Add(entry.NetId))
				{
					LogBaselineFailure("duplicate NetId", entry);
					return false;
				}
				if (!WorldLifecycleBaselineCodec.IsValidTransferEntry(entry))
				{
					LogBaselineFailure("invalid transfer entry", entry);
					return false;
				}
				string failure = entry.Tombstoned
					? null
					: entry.Descriptor.GetSnapshotApplicabilityFailure();
				if (failure != null)
				{
					LogBaselineFailure(failure, entry);
					return false;
				}
			}
			return true;
		}

		private static bool TryApplyExpectedLifecycle(
			IEnumerable<LifecycleRevisionSnapshotEntry> baseline)
		{
			foreach (LifecycleRevisionSnapshotEntry entry in baseline)
				if (!entry.Tombstoned && !entry.Descriptor.TryApplySnapshot())
				{
					LogBaselineFailure("snapshot application failed", entry);
					return false;
				}
			return true;
		}

		private static void LogBaselineFailure(
			string reason, LifecycleRevisionSnapshotEntry entry)
		{
			SpawnPrefabPacket descriptor = entry.Descriptor;
			DebugConsole.LogError(
				$"[LifecycleBaseline] Rejected NetId {entry.NetId}: {reason}; " +
				$"revision={entry.Revision}, tombstone={entry.Tombstoned}, " +
				$"hash={descriptor?.Hash ?? 0}, world={descriptor?.WorldId ?? -1}, " +
				$"bindExisting={descriptor?.BindExistingOnly ?? false}.");
		}

		private static void RemoveUnexpectedLifecycleObjects(
			IEnumerable<LifecycleRevisionSnapshotEntry> baseline)
		{
			var expectedLive = new HashSet<int>(baseline
				.Where(entry => !entry.Tombstoned)
				.Select(entry => entry.NetId));
			RemoveUnexpectedLifecycleObjects(expectedLive);
		}

		internal static void RemoveUnexpectedLifecycleObjects(ISet<int> expectedLive)
		{
			if (expectedLive == null)
				return;
			HashSet<NetworkIdentity> unexpected = ReleaseUnexpectedIdentities(expectedLive);
			foreach (GameObject root in FindUnexpectedRoots(unexpected, expectedLive))
			{
				PreserveExpectedDescendants(root, expectedLive);
				root.SetActive(false);
				Util.KDestroyGameObject(root);
			}
		}

		private static HashSet<NetworkIdentity> ReleaseUnexpectedIdentities(
			ISet<int> expectedLive)
		{
			var unexpected = new HashSet<NetworkIdentity>();
			foreach (var entry in identities.ToArray())
			{
				if (expectedLive.Contains(entry.Key))
					continue;
				AddLiveIdentity(unexpected, entry.Value);
				DuplicantDeathStatePacket.CancelPending(entry.Key);
				Unregister(entry.Value, entry.Key);
				SpawnPrefabPacket.CancelPendingBinding(entry.Key);
			}
			foreach (NetworkIdentity identity in GetUnassignedLiveSnapshot())
			{
				UntrackUnassigned(identity);
				if (!expectedLive.Contains(identity.NetId))
					AddLiveIdentity(unexpected, identity);
			}
			return unexpected;
		}

		private static void AddLiveIdentity(
			ISet<NetworkIdentity> identitiesToAdd, NetworkIdentity identity)
		{
			if (!identity.IsNullOrDestroyed() && !identity.gameObject.IsNullOrDestroyed())
				identitiesToAdd.Add(identity);
		}

		private static IEnumerable<GameObject> FindUnexpectedRoots(
			IEnumerable<NetworkIdentity> unexpected, ISet<int> expectedLive)
		{
			var objects = new HashSet<GameObject>(unexpected
				.Select(identity => identity.gameObject)
				.Where(gameObject => !ContainsExpectedIdentity(gameObject, expectedLive)));
			return objects.Where(gameObject =>
				!HasAncestorInSet(gameObject.transform.parent, objects)).ToArray();
		}

		private static bool ContainsExpectedIdentity(
			GameObject gameObject, ISet<int> expectedLive)
			=> gameObject.GetComponents<NetworkIdentity>()
				.Any(identity => expectedLive.Contains(identity.NetId));

		private static bool HasAncestorInSet(Transform transform, ISet<GameObject> objects)
		{
			for (Transform current = transform; current != null; current = current.parent)
				if (objects.Contains(current.gameObject))
					return true;
			return false;
		}

		private static void PreserveExpectedDescendants(
			GameObject root, ISet<int> expectedLive)
		{
			NetworkIdentity[] expected = root
				.GetComponentsInChildren<NetworkIdentity>(includeInactive: true)
				.Where(identity => identity.gameObject != root
				                   && expectedLive.Contains(identity.NetId))
				.ToArray();
			foreach (NetworkIdentity identity in expected)
			{
				if (HasExpectedAncestor(identity.transform.parent, root.transform, expectedLive))
					continue;
				identity.transform.SetParent(root.transform.parent, worldPositionStays: true);
				DebugConsole.Log(
					$"[Lifecycle] Preserved expected NetId {identity.NetId} from extra parent");
			}
		}

		private static bool HasExpectedAncestor(
			Transform transform, Transform root, ISet<int> expectedLive)
		{
			for (Transform current = transform; current != null && current != root;
			     current = current.parent)
			{
				NetworkIdentity identity = current.GetComponent<NetworkIdentity>();
				if (identity != null && expectedLive.Contains(identity.NetId))
					return true;
			}
			return false;
		}
	}
}
