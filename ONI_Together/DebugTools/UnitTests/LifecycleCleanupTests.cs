#if DEBUG
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Patches.KleiPatches;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class LifecycleCleanupTests
	{
		[UnitTest(name: "Lifecycle descriptor rejects non-prefab objects", category: "Sync")]
		public static UnitTestResult DescriptorRejectsNonPrefabObject()
		{
			GameObject transient = CreateIdentity("ClusterMapPeekAnim(Clone)", 4);
			try
			{
				SpawnPrefabPacket descriptor = SpawnPrefabPacket.FromIdentity(
					transient.GetComponent<NetworkIdentity>());
				return descriptor == null
					? UnitTestResult.Pass("Transient animation objects cannot enter a world baseline")
					: UnitTestResult.Fail("A transient animation object produced a lifecycle descriptor");
			}
			finally
			{
				DestroyIfLive(transient);
			}
		}

		[UnitTest(
			name: "Lifecycle cleanup preserves expected child of extra parent",
			category: "Sync")]
		public static UnitTestResult CleanupPreservesExpectedChild()
		{
			int parentId = AvailableNetId(-910001);
			int childId = AvailableNetId(parentId - 1);
			int siblingId = AvailableNetId(childId - 1);
			GameObject parent = CreateIdentity("ExtraParent", parentId);
			GameObject child = CreateIdentity("ExpectedChild", childId, parent.transform);
			GameObject sibling = CreateIdentity("ExtraSibling", siblingId, parent.transform);
			var previousRevisions = NetworkIdentityRegistry.GetLifecycleRevisionSnapshot();
			ulong previousAuthority = NetworkIdentityRegistry.AuthorityRevisionForTests;
			try
			{
				Register(parent, parentId);
				Register(child, childId);
				Register(sibling, siblingId);
				var expected = new HashSet<int>(NetworkIdentityRegistry.AllIdentities
					.Select(identity => identity.NetId));
				expected.Remove(parentId);
				expected.Remove(siblingId);

				NetworkIdentityRegistry.RemoveUnexpectedLifecycleObjects(expected);
				if (child.transform.parent != null || !child.activeInHierarchy
				    || !NetworkIdentityRegistry.TryGet(childId, out NetworkIdentity registered)
				    || registered.gameObject != child)
					return UnitTestResult.Fail("Expected child stayed under the doomed extra parent");
				if (NetworkIdentityRegistry.Exists(parentId)
				    || NetworkIdentityRegistry.Exists(siblingId) || parent.activeSelf)
					return UnitTestResult.Fail("Unexpected lifecycle roots were not removed");
				return UnitTestResult.Pass("Expected child was detached before extra-tree cleanup");
			}
			finally
			{
				Unregister(child, childId);
				DestroyIfLive(child);
				if (parent != null && parent.activeSelf)
					DestroyIfLive(parent);
				NetworkIdentityRegistry.RestoreLifecycleRevisionStateForTests(
					previousRevisions, previousAuthority);
			}
		}

		[UnitTest(
			name: "Keyframe final validation waits past apply frame",
			category: "Sync")]
		public static UnitTestResult FinalValidationWaitsPastApplyFrame()
		{
			return !SoakHashDomainKeyframeTracker.IsDeferredValidationFrame(12, 12)
			       && !SoakHashDomainKeyframeTracker.IsDeferredValidationFrame(12, 13)
			       && SoakHashDomainKeyframeTracker.IsDeferredValidationFrame(12, 14)
				? UnitTestResult.Pass("Two frame boundaries settle deferred destruction before final keyframe ACK")
				: UnitTestResult.Fail("Keyframe can still report success in its apply frame");
		}

		[UnitTest(name: "Pending destruction cannot be claimed", category: "Sync")]
		public static UnitTestResult PendingDestructionCannotBeClaimed()
		{
			int targetNetId = AvailableNetId(-925001);
			GameObject gameObject = CreateTaggedIdentity(
				"PendingDestroyIdentity", targetNetId, "PendingDestroyPrefab");
			NetworkIdentity identity = gameObject.GetComponent<NetworkIdentity>();
			var previousRevisions = NetworkIdentityRegistry.GetLifecycleRevisionSnapshot();
			ulong previousAuthority = NetworkIdentityRegistry.AuthorityRevisionForTests;
			try
			{
				identity.NetId = 0;
				identity.ExpectedAuthorityNetId = targetNetId;
				NetworkIdentityRegistry.TrackUnassigned(identity);
				KDestroyGameObjectNetworkIdentityPatch.Prefix(gameObject);
				bool claimed = NetworkIdentityRegistry.TryClaimAuthorityBinding(
					gameObject.PrefabID().GetHashCode(), gameObject.transform.position,
					-1, targetNetId, out _);
				return !claimed && !NetworkIdentityRegistry.Exists(targetNetId)
					? UnitTestResult.Pass("A queued cleanup cannot acquire an authoritative NetId")
					: UnitTestResult.Fail("A queued cleanup acquired an authoritative NetId");
			}
			finally
			{
				NetworkIdentityRegistry.UntrackUnassigned(identity);
				DestroyIfLive(gameObject);
				NetworkIdentityRegistry.RestoreLifecycleRevisionStateForTests(
					previousRevisions, previousAuthority);
			}
		}

		[UnitTest(name: "Unavailable direct-discovery candidate is rejected", category: "Sync")]
		public static UnitTestResult UnavailableDirectDiscoveryCandidateIsRejected()
		{
			GameObject gameObject = CreateIdentity("UnavailableDirectCandidate", 0);
			try
			{
				NetworkIdentity identity = gameObject.GetComponent<NetworkIdentity>();
				bool liveAccepted = NetworkIdentityRegistry.IsAvailableBindingCandidate(gameObject);
				identity.MarkDestructionPending();
				bool pendingRejected = !NetworkIdentityRegistry.IsAvailableBindingCandidate(gameObject);
				return liveAccepted && pendingRejected
					? UnitTestResult.Pass("Grid, tracker, receptacle, and POI discovery share the live-candidate gate")
					: UnitTestResult.Fail("Direct object discovery can still reuse a pending-destruction identity");
			}
			finally
			{
				DestroyIfLive(gameObject);
			}
		}

		[UnitTest(name: "Persistent authority waiters are not age-pruned", category: "Sync")]
		public static UnitTestResult PersistentAuthorityWaitersAreNotAgePruned()
		{
			bool genericExpired = NetworkIdentityRegistry.ShouldPruneUnassigned(
				0, false, 31f, 30f);
			bool exactWaiterRetained = !NetworkIdentityRegistry.ShouldPruneUnassigned(
				42, false, 300f, 30f);
			bool persistentWaiterRetained = !NetworkIdentityRegistry.ShouldPruneUnassigned(
				0, true, 300f, 30f);
			return genericExpired && exactWaiterRetained && persistentWaiterRetained
				? UnitTestResult.Pass("Only unreserved transient client spawns expire")
				: UnitTestResult.Fail("A slow world load can prune a persistent authority waiter");
		}

		[UnitTest(name: "Registered lifecycle mutation rollback restores owner", category: "Sync")]
		public static UnitTestResult RegisteredMutationRollbackRestoresOwner()
		{
			int netId = AvailableNetId(-925501);
			GameObject gameObject = CreateIdentity("RegisteredMutationRollback", netId);
			NetworkIdentity identity = gameObject.GetComponent<NetworkIdentity>();
			var previousRevisions = NetworkIdentityRegistry.GetLifecycleRevisionSnapshot();
			ulong previousAuthority = NetworkIdentityRegistry.AuthorityRevisionForTests;
			try
			{
				Register(gameObject, netId);
				ulong revision = NetworkIdentityRegistry.BeginLifecycle(netId);
				Vector3 position = gameObject.transform.position;
				bool active = gameObject.activeSelf;
				if (!NetworkIdentityRegistry.TryBeginRegisteredMutation(
					    identity, netId, out NetworkIdentityRegistry.IdentityClaim mutation))
					return UnitTestResult.Fail("Could not begin registered mutation transaction");
				NetworkIdentityRegistry.TryAcceptLifecycleRevision(
					netId, revision + 100, tombstone: false);
				identity.LifecycleRevision = revision + 100;
				gameObject.transform.position = position + Vector3.one;
				gameObject.SetActive(!active);
				NetworkIdentityRegistry.RollbackClaim(mutation);
				bool ownerRestored = NetworkIdentityRegistry.TryGet(
					netId, out NetworkIdentity restored) && ReferenceEquals(restored, identity);
				return ownerRestored && identity.NetId == netId
				       && NetworkIdentityRegistry.GetLastLifecycleRevision(netId) == revision
				       && identity.LifecycleRevision != revision + 100
				       && gameObject.transform.position == position
				       && gameObject.activeSelf == active
					? UnitTestResult.Pass("Failed occupied materialization restores registry, journal, transform, and active state")
					: UnitTestResult.Fail("Registered mutation rollback left partial lifecycle state");
			}
			finally
			{
				Unregister(gameObject, netId);
				DestroyIfLive(gameObject);
				NetworkIdentityRegistry.RestoreLifecycleRevisionStateForTests(
					previousRevisions, previousAuthority);
			}
		}

		[UnitTest(name: "Spawn replacement displacement is rollback-safe", category: "Sync")]
		public static UnitTestResult SpawnReplacementDisplacementIsRollbackSafe()
		{
			int netId = AvailableNetId(-925751);
			GameObject gameObject = CreateIdentity("SpawnDisplacementRollback", netId);
			NetworkIdentity identity = gameObject.GetComponent<NetworkIdentity>();
			var previousRevisions = NetworkIdentityRegistry.GetLifecycleRevisionSnapshot();
			ulong previousAuthority = NetworkIdentityRegistry.AuthorityRevisionForTests;
			NetworkIdentityRegistry.IdentityClaim displaced = null;
			try
			{
				Register(gameObject, netId);
				ulong revision = NetworkIdentityRegistry.BeginLifecycle(netId);
				MethodInfo tryDisplace = typeof(SpawnPrefabPacket).GetMethod(
					"TryDisplace", BindingFlags.Static | BindingFlags.NonPublic);
				object[] arguments = { identity, null };
				bool succeeded = (bool)(tryDisplace?.Invoke(null, arguments) ?? false);
				displaced = arguments[1] as NetworkIdentityRegistry.IdentityClaim;
				bool releasedAlive = succeeded && displaced != null
				                     && !NetworkIdentityRegistry.Exists(netId)
				                     && !gameObject.IsNullOrDestroyed() && gameObject.activeSelf;
				NetworkIdentityRegistry.RollbackClaim(displaced);
				displaced = null;
				bool restored = NetworkIdentityRegistry.TryGet(
					netId, out NetworkIdentity owner) && ReferenceEquals(owner, identity);
				return releasedAlive && restored
				       && NetworkIdentityRegistry.GetLastLifecycleRevision(netId) == revision
				       && gameObject.activeSelf
					? UnitTestResult.Pass("Failed replacement can restore the live NetId owner")
					: UnitTestResult.Fail("Displacement destroyed or lost the rollback owner");
			}
			finally
			{
				NetworkIdentityRegistry.RollbackClaim(displaced);
				Unregister(gameObject, netId);
				DestroyIfLive(gameObject);
				NetworkIdentityRegistry.RestoreLifecycleRevisionStateForTests(
					previousRevisions, previousAuthority);
			}
		}

		[UnitTest(name: "Authority binding never steals registered identity", category: "Sync")]
		public static UnitTestResult AuthorityBindingNeverStealsRegisteredIdentity()
		{
			int registeredNetId = AvailableNetId(-926001);
			int targetNetId = AvailableNetId(registeredNetId - 1);
			GameObject gameObject = CreateTaggedIdentity(
				"RegisteredAuthorityIdentity", registeredNetId, "RegisteredAuthorityPrefab");
			NetworkIdentity identity = gameObject.GetComponent<NetworkIdentity>();
			var previousRevisions = NetworkIdentityRegistry.GetLifecycleRevisionSnapshot();
			ulong previousAuthority = NetworkIdentityRegistry.AuthorityRevisionForTests;
			try
			{
				Register(gameObject, registeredNetId);
				identity.ExpectedAuthorityNetId = targetNetId;
				bool claimed = NetworkIdentityRegistry.TryClaimAuthorityBinding(
					gameObject.PrefabID().GetHashCode(), gameObject.transform.position,
					-1, targetNetId, out _);
				bool originalPreserved = NetworkIdentityRegistry.TryGet(
					registeredNetId, out NetworkIdentity registered)
				                         && ReferenceEquals(registered, identity);
				return !claimed && originalPreserved && !NetworkIdentityRegistry.Exists(targetNetId)
					? UnitTestResult.Pass("Only the exact unassigned waiter can receive an authority binding")
					: UnitTestResult.Fail("Authority binding stole an already registered identity");
			}
			finally
			{
				Unregister(gameObject, registeredNetId);
				Unregister(gameObject, targetNetId);
				DestroyIfLive(gameObject);
				NetworkIdentityRegistry.RestoreLifecycleRevisionStateForTests(
					previousRevisions, previousAuthority);
			}
		}

		[UnitTest(name: "Failed authority claim restores unassigned binding", category: "Sync")]
		public static UnitTestResult FailedAuthorityClaimRollsBack()
		{
			int targetNetId = AvailableNetId(-927001);
			GameObject gameObject = CreateTaggedIdentity(
				"RollbackAuthorityIdentity", 0, "RollbackAuthorityPrefab");
			NetworkIdentity identity = gameObject.GetComponent<NetworkIdentity>();
			var previousRevisions = NetworkIdentityRegistry.GetLifecycleRevisionSnapshot();
			ulong previousAuthority = NetworkIdentityRegistry.AuthorityRevisionForTests;
			ulong previousTargetRevision = NetworkIdentityRegistry.GetLastLifecycleRevision(targetNetId);
			bool previousTargetTombstone = NetworkIdentityRegistry.IsLifecycleTombstoned(targetNetId);
			Vector3 previousPosition = gameObject.transform.position;
			bool previousActiveSelf = gameObject.activeSelf;
			try
			{
				identity.ExpectedAuthorityNetId = targetNetId;
				NetworkIdentityRegistry.TrackUnassigned(identity);
				bool claimed = NetworkIdentityRegistry.TryBeginAuthorityBindingClaim(
					gameObject.PrefabID().GetHashCode(), gameObject.transform.position,
					-1, targetNetId, out NetworkIdentityRegistry.IdentityClaim claim);
				if (!claimed || !NetworkIdentityRegistry.Exists(targetNetId))
					return UnitTestResult.Fail("Test claim did not acquire the target NetId");
				gameObject.transform.position = previousPosition + Vector3.one;
				gameObject.SetActive(!previousActiveSelf);
				NetworkIdentityRegistry.RollbackClaim(claim);
				bool tracked = NetworkIdentityRegistry.GetUnassignedLiveSnapshot().Contains(identity);
				return identity.NetId == 0 && identity.ExpectedAuthorityNetId == targetNetId
				       && !NetworkIdentityRegistry.Exists(targetNetId) && tracked
				       && gameObject.transform.position == previousPosition
				       && gameObject.activeSelf == previousActiveSelf
				       && NetworkIdentityRegistry.GetLastLifecycleRevision(targetNetId) == previousTargetRevision
				       && NetworkIdentityRegistry.IsLifecycleTombstoned(targetNetId) == previousTargetTombstone
					? UnitTestResult.Pass("Failed materialization restores the exact unassigned waiter")
					: UnitTestResult.Fail("Failed materialization left a partial authority binding");
			}
			finally
			{
				NetworkIdentityRegistry.UntrackUnassigned(identity);
				Unregister(gameObject, targetNetId);
				DestroyIfLive(gameObject);
				NetworkIdentityRegistry.RestoreLifecycleRevisionStateForTests(
					previousRevisions, previousAuthority);
			}
		}

		[UnitTest(name: "Authority binding commits exact unassigned waiter", category: "Sync")]
		public static UnitTestResult AuthorityBindingCommitsExactWaiter()
		{
			int targetNetId = AvailableNetId(-928001);
			GameObject gameObject = CreateTaggedIdentity(
				"CommittedAuthorityIdentity", 0, "CommittedAuthorityPrefab");
			NetworkIdentity identity = gameObject.GetComponent<NetworkIdentity>();
			var previousRevisions = NetworkIdentityRegistry.GetLifecycleRevisionSnapshot();
			ulong previousAuthority = NetworkIdentityRegistry.AuthorityRevisionForTests;
			try
			{
				identity.ExpectedAuthorityNetId = targetNetId;
				NetworkIdentityRegistry.TrackUnassigned(identity);
				bool claimed = NetworkIdentityRegistry.TryBeginAuthorityBindingClaim(
					gameObject.PrefabID().GetHashCode(), gameObject.transform.position,
					-1, targetNetId, out _);
				ulong revision = NetworkIdentityRegistry.GetLastLifecycleRevision(targetNetId);
				bool bound = NetworkIdentityRegistry.TryBindAuthoritativeLifecycle(
					gameObject, targetNetId, revision);
				bool tracked = NetworkIdentityRegistry.GetUnassignedLiveSnapshot().Contains(identity);
				return claimed && bound && revision != 0 && !tracked
				       && NetworkIdentityRegistry.IsRegistered(identity, targetNetId)
				       && identity.LifecycleRevision == revision
					? UnitTestResult.Pass("Exact waiter committed the authoritative lifecycle")
					: UnitTestResult.Fail("Exact waiter did not commit the authoritative lifecycle");
			}
			finally
			{
				NetworkIdentityRegistry.UntrackUnassigned(identity);
				Unregister(gameObject, targetNetId);
				DestroyIfLive(gameObject);
				NetworkIdentityRegistry.RestoreLifecycleRevisionStateForTests(
					previousRevisions, previousAuthority);
			}
		}

		[UnitTest(name: "Generic claim cannot steal mismatched authority waiter", category: "Sync")]
		public static UnitTestResult GenericClaimCannotStealAuthorityWaiter()
		{
			int expectedNetId = AvailableNetId(-928501);
			int incomingNetId = AvailableNetId(expectedNetId - 1);
			GameObject gameObject = CreateTaggedIdentity(
				"ReservedAuthorityIdentity", 0, "ReservedAuthorityPrefab");
			NetworkIdentity identity = gameObject.GetComponent<NetworkIdentity>();
			var previousRevisions = NetworkIdentityRegistry.GetLifecycleRevisionSnapshot();
			ulong previousAuthority = NetworkIdentityRegistry.AuthorityRevisionForTests;
			try
			{
				identity.ExpectedAuthorityNetId = expectedNetId;
				NetworkIdentityRegistry.TrackUnassigned(identity);
				bool claimed = NetworkIdentityRegistry.TryClaimUnassigned(
					gameObject.PrefabID().GetHashCode(), gameObject.transform.position,
					-1, incomingNetId, out _);
				return !claimed && identity.NetId == 0
				       && identity.ExpectedAuthorityNetId == expectedNetId
				       && !NetworkIdentityRegistry.Exists(incomingNetId)
					? UnitTestResult.Pass("Generic claim preserved the waiter reserved for another NetId")
					: UnitTestResult.Fail("Generic claim stole a waiter reserved for another NetId");
			}
			finally
			{
				NetworkIdentityRegistry.UntrackUnassigned(identity);
				Unregister(gameObject, incomingNetId);
				DestroyIfLive(gameObject);
				NetworkIdentityRegistry.RestoreLifecycleRevisionStateForTests(
					previousRevisions, previousAuthority);
			}
		}

		[UnitTest(name: "Pending destruction yields authority registration", category: "Sync")]
		public static UnitTestResult PendingDestructionYieldsAuthorityRegistration()
		{
			int targetNetId = AvailableNetId(-929001);
			GameObject stale = CreateTaggedIdentity(
				"StaleAuthorityIdentity", targetNetId, "YieldedAuthorityPrefab");
			GameObject waiter = CreateTaggedIdentity(
				"ReplacementAuthorityIdentity", 0, "YieldedAuthorityPrefab");
			NetworkIdentity staleIdentity = stale.GetComponent<NetworkIdentity>();
			NetworkIdentity waiterIdentity = waiter.GetComponent<NetworkIdentity>();
			var previousRevisions = NetworkIdentityRegistry.GetLifecycleRevisionSnapshot();
			ulong previousAuthority = NetworkIdentityRegistry.AuthorityRevisionForTests;
			try
			{
				Register(stale, targetNetId);
				staleIdentity.MarkDestructionPending();
				waiterIdentity.ExpectedAuthorityNetId = targetNetId;
				NetworkIdentityRegistry.TrackUnassigned(waiterIdentity);
				bool released = NetworkIdentityRegistry.ReleaseUnavailableRegistration(targetNetId);
				bool claimed = NetworkIdentityRegistry.TryBeginAuthorityBindingClaim(
					waiter.PrefabID().GetHashCode(), waiter.transform.position,
					-1, targetNetId, out NetworkIdentityRegistry.IdentityClaim claim);
				bool replacementOwnsTarget = NetworkIdentityRegistry.IsRegistered(
					waiterIdentity, targetNetId);
				NetworkIdentityRegistry.RollbackClaim(claim);
				return released && claimed && replacementOwnsTarget
					? UnitTestResult.Pass("Doomed registration yielded to the exact authority waiter")
					: UnitTestResult.Fail("Doomed registration blocked the exact authority waiter");
			}
			finally
			{
				NetworkIdentityRegistry.UntrackUnassigned(waiterIdentity);
				Unregister(stale, targetNetId);
				Unregister(waiter, targetNetId);
				DestroyIfLive(stale);
				DestroyIfLive(waiter);
				NetworkIdentityRegistry.RestoreLifecycleRevisionStateForTests(
					previousRevisions, previousAuthority);
			}
		}

		[UnitTest(name: "Terminal lifecycle cleanup is idempotent", category: "Sync")]
		public static UnitTestResult TerminalCleanupCannotReviveIdentity()
		{
			int netId = AvailableNetId(-920001);
			GameObject gameObject = CreateIdentity("TerminalIdentity", netId);
			NetworkIdentity identity = gameObject.GetComponent<NetworkIdentity>();
			var previousRevisions = NetworkIdentityRegistry.GetLifecycleRevisionSnapshot();
			ulong previousAuthority = NetworkIdentityRegistry.AuthorityRevisionForTests;
			try
			{
				Register(gameObject, netId);
				NetworkIdentityRegistry.BeginLifecycle(netId);
				ulong tombstone = NetworkIdentityRegistry.EndLifecycle(netId);
				identity.MarkLifecycleTerminalForTests();
				NetworkIdentityRegistry.Unregister(identity, netId);
				NetworkIdentity resolved = gameObject.GetNetIdentity();
				ulong repeated = NetworkIdentityRegistry.EndLifecycle(netId);
				if (resolved != null || NetworkIdentityRegistry.Exists(netId)
				    || repeated != tombstone
				    || NetworkIdentityRegistry.GetLastLifecycleRevision(netId) != tombstone)
					return UnitTestResult.Fail("Delayed cleanup revived or revised a terminal identity");
				return UnitTestResult.Pass("Terminal identity stays tombstoned across delayed cleanup");
			}
			finally
			{
				DestroyIfLive(gameObject);
				NetworkIdentityRegistry.RestoreLifecycleRevisionStateForTests(
					previousRevisions, previousAuthority);
			}
		}

		private static GameObject CreateIdentity(
			string name, int netId, Transform parent = null)
		{
			var gameObject = new GameObject(name);
			gameObject.transform.SetParent(parent, worldPositionStays: true);
			gameObject.AddComponent<NetworkIdentity>().NetId = netId;
			return gameObject;
		}

		private static GameObject CreateTaggedIdentity(
			string name, int netId, string prefabTag)
		{
			GameObject gameObject = CreateIdentity(name, netId);
			gameObject.AddComponent<KPrefabID>().PrefabTag = new Tag(prefabTag);
			return gameObject;
		}

		private static int AvailableNetId(int candidate)
		{
			while (NetworkIdentityRegistry.Exists(candidate))
				candidate--;
			return candidate;
		}

		private static void Register(GameObject gameObject, int netId)
		{
			NetworkIdentity identity = gameObject.GetComponent<NetworkIdentity>();
			NetworkIdentityRegistry.RegisterExisting(identity, netId);
		}

		private static void Unregister(GameObject gameObject, int netId)
		{
			if (gameObject != null)
				NetworkIdentityRegistry.Unregister(
					gameObject.GetComponent<NetworkIdentity>(), netId);
		}

		private static void DestroyIfLive(GameObject gameObject)
		{
			if (gameObject != null && !gameObject.IsNullOrDestroyed())
				Util.KDestroyGameObject(gameObject);
		}
	}
}
#endif
