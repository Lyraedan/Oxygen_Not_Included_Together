using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using System;
using System.Collections.Generic;
using System.Linq;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking
{
	public static partial class NetworkIdentityRegistry
	{
		public readonly struct LifecycleRevisionSnapshotEntry
		{
			public readonly int NetId;
			public readonly ulong Revision;
			public readonly bool Tombstoned;
			public readonly SpawnPrefabPacket Descriptor;

			public LifecycleRevisionSnapshotEntry(int netId, ulong revision, bool tombstoned)
				: this(netId, revision, tombstoned, null)
			{
			}

			public LifecycleRevisionSnapshotEntry(
				int netId, ulong revision, bool tombstoned, SpawnPrefabPacket descriptor)
			{
				NetId = netId;
				Revision = revision;
				Tombstoned = tombstoned;
				Descriptor = descriptor;
			}
		}

		public readonly struct LifecycleMembershipValidationResult
		{
			public readonly bool BaselineValid;
			public readonly int MissingLiveCount;
			public readonly int UnexpectedLiveCount;
			public readonly int TombstonedLiveCount;
			public readonly int UnassignedLiveCount;

			internal LifecycleMembershipValidationResult(
				bool baselineValid, int missingLiveCount, int unexpectedLiveCount,
				int tombstonedLiveCount, int unassignedLiveCount = 0)
			{
				BaselineValid = baselineValid;
				MissingLiveCount = missingLiveCount;
				UnexpectedLiveCount = unexpectedLiveCount;
				TombstonedLiveCount = tombstonedLiveCount;
				UnassignedLiveCount = unassignedLiveCount;
			}

			public bool IsValid => BaselineValid && MissingLiveCount == 0 &&
			                       UnexpectedLiveCount == 0 && TombstonedLiveCount == 0 &&
			                       UnassignedLiveCount == 0;
		}

		private static readonly Dictionary<int, NetworkIdentity> identities = new Dictionary<int, NetworkIdentity>();
		private static readonly Dictionary<NetworkIdentity, float> unassigned = new Dictionary<NetworkIdentity, float>();
		private static readonly Dictionary<int, ulong> lifecycleRevisions = new();
		private static readonly HashSet<int> lifecycleTombstones = new();
		private static readonly Dictionary<int, ulong> storageSnapshotRevisions = new();
		private static readonly Dictionary<int, ulong> storageEventRevisions = new();
		private static readonly Dictionary<int, ulong> storageItemRevisions = new();
		private static readonly Dictionary<(int NetId, string Domain), ulong> stateRevisions = new();
		private static int nextAllocatedNetId = 1;
		private static ulong nextAuthorityRevision;
		private static bool lifecyclePruneFrozen;

		private static int _lookupFailCount = 0;
		private static float _lastFailLogTime = 0f;

		public static int Count => identities?.Count ?? 0;

		internal static NetworkIdentity[] GetUnassignedLiveSnapshot()
		{
			return unassigned.Keys.Where(identity =>
				!identity.IsNullOrDestroyed()
				&& !identity.gameObject.IsNullOrDestroyed()).ToArray();
		}

		public static int Register(NetworkIdentity entity)
		{
			using var _ = Profiler.Scope();

			int id;
			do
			{
				id = nextAllocatedNetId++;
				if (nextAllocatedNetId <= 0)
					nextAllocatedNetId = 1;
			} while (id == 0 || identities.ContainsKey(id));

			identities[id] = entity;
			BeginLifecycle(id);
			return id;
		}

		public static bool Unregister(NetworkIdentity entity, int netId)
		{
			using var _ = Profiler.Scope();

			if (!identities.TryGetValue(netId, out NetworkIdentity registered)
			    || !ReferenceEquals(registered, entity))
			{
				return false;
			}

			return identities.Remove(netId);
		}


		public static bool RegisterExisting(NetworkIdentity entity, int netId)
		{
			using var _ = Profiler.Scope();

			if (netId == 0)
				return false;

			if (!identities.TryGetValue(netId, out NetworkIdentity existing))
			{
				identities[netId] = entity;
				if (!MultiplayerSession.InSession || MultiplayerSession.IsHost)
					BeginLifecycle(netId);
				return true;
			}

			if (ReferenceEquals(existing, entity))
				return true;

			DebugConsole.LogError($"[NetEntityRegistry] NetId collision {netId}: {existing.name} vs {entity.name}");
			return false;
		}

		public static bool RegisterOverride(NetworkIdentity entity, int netId)
		{
			using var _ = Profiler.Scope();

			if (netId == 0)
				return false;
			ReleaseUnavailableClientRegistration(netId);

			if (identities.TryGetValue(netId, out NetworkIdentity existing)
			    && !ReferenceEquals(existing, entity))
			{
				DebugConsole.LogError($"[NetEntityRegistry] Refusing to overwrite NetId {netId} owned by {existing.name}");
				return false;
			}

			identities[netId] = entity;
			return true;
		}

		public static bool IsRegistered(NetworkIdentity entity, int netId)
			=> netId != 0 && identities.TryGetValue(netId, out NetworkIdentity existing)
			   && ReferenceEquals(existing, entity);

		public static void TrackUnassigned(NetworkIdentity entity)
		{
			if (entity != null)
			{
				unassigned[entity] = Time.unscaledTime;
				SpawnPrefabPacket.TryApplyPendingBindings();
			}
		}

		public static void UntrackUnassigned(NetworkIdentity entity)
		{
			if (entity != null)
				unassigned.Remove(entity);
		}

		public static bool TryClaimUnassigned(int prefabHash, Vector3 position, int netId, out GameObject claimed)
			=> TryClaimUnassigned(prefabHash, position, -1, netId, out claimed);

		public static bool TryClaimUnassigned(int prefabHash, Vector3 position, int worldId, int netId, out GameObject claimed)
		{
			bool started = TryBeginUnassignedClaim(
				prefabHash, position, worldId, netId, out IdentityClaim claim);
			claimed = claim?.GameObject;
			return started;
		}

		internal static bool TryBeginUnassignedClaim(
			int prefabHash, Vector3 position, int worldId, int netId,
			out IdentityClaim claim)
		{
			using var _ = Profiler.Scope();
			claim = null;
			NetworkIdentity best = null;
			float bestDistance = 4f;
			bool ambiguous = false;
			foreach (var entry in unassigned.ToArray())
			{
				NetworkIdentity identity = entry.Key;
				if (identity.IsNullOrDestroyed() || identity.gameObject.IsNullOrDestroyed())
				{
					unassigned.Remove(identity);
					continue;
				}

				if (identity.IsUnavailableForBinding
				    || IsRegistered(identity, identity.NetId)
				    || identity.ExpectedAuthorityNetId != 0
				       && identity.ExpectedAuthorityNetId != netId
				    || identity.gameObject.PrefabID().GetHashCode() != prefabHash)
					continue;
				if (worldId >= 0 && (identity.gameObject.GetMyWorld()?.id ?? -1) != worldId)
					continue;
				float distance = (identity.transform.position - position).sqrMagnitude;
				if (best == null || distance < bestDistance - 0.0001f)
				{
					bestDistance = distance;
					best = identity;
					ambiguous = false;
				}
				else if (Mathf.Abs(distance - bestDistance) <= 0.0001f)
					ambiguous = true;
			}

			if (best == null || ambiguous || bestDistance > 4f)
				return false;
			if (!TryBeginClaim(best, netId, out claim))
				return false;
			best.transform.position = position;
			return true;
		}

		public static bool TryClaimAuthorityBinding(int prefabHash, Vector3 position, int worldId,
			int netId, out GameObject claimed)
		{
			bool started = TryBeginAuthorityBindingClaim(
				prefabHash, position, worldId, netId, out IdentityClaim claim);
			claimed = claim?.GameObject;
			return started;
		}

		internal static bool TryBeginAuthorityBindingClaim(
			int prefabHash, Vector3 position, int worldId, int netId,
			out IdentityClaim claim)
		{
			claim = null;
			NetworkIdentity best = FindUniqueAuthorityBinding(
				prefabHash, position, worldId, netId);
			if (best == null)
				return false;
			if (!TryBeginClaim(best, netId, out claim))
				return false;
			return true;
		}

		private static bool TryBeginClaim(
			NetworkIdentity identity, int netId, out IdentityClaim claim)
		{
			claim = null;
			if (identity == null || !unassigned.ContainsKey(identity))
				return false;
			var pendingClaim = new IdentityClaim(identity, netId);
			if (!identity.OverrideNetId(netId))
				return false;
			claim = pendingClaim;
			return true;
		}

		internal static bool TryBeginRegisteredMutation(
			NetworkIdentity identity, int netId, out IdentityClaim claim)
		{
			claim = null;
			if (identity.IsNullOrDestroyed() || identity.gameObject.IsNullOrDestroyed()
			    || identity.IsUnavailableForBinding || !IsRegistered(identity, netId))
				return false;
			claim = new IdentityClaim(identity, netId);
			return true;
		}

		internal static void RollbackClaim(IdentityClaim claim)
		{
			if (claim == null)
				return;
			NetworkIdentity identity = claim?.Identity;
			Unregister(identity, claim.ClaimedNetId);
			RestoreLifecycleRevisionState(claim.ClaimedNetId, claim.PreviousLifecycleState);
			if (identity.IsNullOrDestroyed() || identity.gameObject.IsNullOrDestroyed())
				return;
			identity.RestoreBindingState(claim.PreviousState);
			if (claim.WasRegistered && claim.PreviousState.NetId != 0
			    && !RegisterOverride(identity, claim.PreviousState.NetId))
			{
				DebugConsole.LogError(
					$"[NetEntityRegistry] Failed to restore NetId {claim.PreviousState.NetId}");
			}
			identity.transform.position = claim.PreviousPosition;
			identity.gameObject.SetActive(claim.PreviousActiveSelf);
			if (!claim.WasTracked || identity.IsUnavailableForBinding)
				return;
			unassigned[identity] = claim.TrackedAt;
		}

		internal static bool ReleaseUnavailableRegistration(int netId)
		{
			if (!identities.TryGetValue(netId, out NetworkIdentity identity))
				return false;
			if (!identity.IsNullOrDestroyed() && !identity.gameObject.IsNullOrDestroyed()
			    && !identity.IsUnavailableForBinding)
				return false;
			return identities.Remove(netId);
		}

		internal static bool CanClaimAuthorityBinding(
			int prefabHash, Vector3 position, int worldId, int netId)
			=> FindUniqueAuthorityBinding(prefabHash, position, worldId, netId) != null;

		private static NetworkIdentity FindUniqueAuthorityBinding(
			int prefabHash, Vector3 position, int worldId, int netId)
		{
			NetworkIdentity best = null;
			float bestDistance = 4f;
			bool ambiguous = false;
			foreach (NetworkIdentity identity in unassigned.Keys.ToArray())
			{
				if (identity.IsNullOrDestroyed() || identity.gameObject.IsNullOrDestroyed()
				    || identity.IsUnavailableForBinding
				    || IsRegistered(identity, identity.NetId)
				    || identity.ExpectedAuthorityNetId != netId
				    || identity.gameObject.PrefabID().GetHashCode() != prefabHash
				    || worldId >= 0 && (identity.gameObject.GetMyWorld()?.id ?? -1) != worldId)
					continue;
				float distance = (identity.transform.position - position).sqrMagnitude;
				if (best == null || distance < bestDistance - 0.0001f)
				{
					best = identity;
					bestDistance = distance;
					ambiguous = false;
				}
				else if (Mathf.Abs(distance - bestDistance) <= 0.0001f)
					ambiguous = true;
			}
			return best != null && !ambiguous && bestDistance <= 4f ? best : null;
		}

		public static void PruneUnassigned(float maximumAgeSeconds = 30f)
		{
			if (lifecyclePruneFrozen
			    || !MultiplayerSession.InSession || !MultiplayerSession.IsClient)
				return;

			float now = Time.unscaledTime;
			foreach (var entry in unassigned.ToArray())
			{
				NetworkIdentity identity = entry.Key;
				if (identity.IsNullOrDestroyed() || identity.gameObject.IsNullOrDestroyed())
				{
					unassigned.Remove(identity);
					continue;
				}
				if (!ShouldPruneUnassigned(
					    identity.ExpectedAuthorityNetId,
					    identity.RequiresExistingBinding,
					    now - entry.Value, maximumAgeSeconds))
					continue;

				unassigned.Remove(identity);
				DebugConsole.LogWarning($"[NetEntityRegistry] Removing unconfirmed client-side spawn {identity.name}");
				Util.KDestroyGameObject(identity.gameObject);
			}
		}

		internal static void SetLifecyclePruneFrozen(bool frozen)
			=> lifecyclePruneFrozen = frozen;

		internal static bool ShouldPruneUnassigned(
			int expectedAuthorityNetId, bool requiresExistingBinding,
			float ageSeconds, float maximumAgeSeconds)
			=> expectedAuthorityNetId == 0 && !requiresExistingBinding
			   && ageSeconds >= maximumAgeSeconds;

		internal static bool IsAvailableBindingCandidate(GameObject gameObject)
		{
			if (gameObject == null || gameObject.IsNullOrDestroyed())
				return false;
			return !gameObject.TryGetComponent(out NetworkIdentity identity)
			       || !identity.IsUnavailableForBinding;
		}

		public static bool Exists(int netId)
		{
			ReleaseUnavailableClientRegistration(netId);
			return identities.ContainsKey(netId);
		}

		public static bool TryGet(int netId, out NetworkIdentity entity)
		{
			using var _ = Profiler.Scope();

			ReleaseUnavailableClientRegistration(netId);
			bool found = identities.TryGetValue(netId, out entity);
			if (!found)
			{
				_lookupFailCount++;
				if (_lookupFailCount <= 3 || _lookupFailCount % 500 == 0 || Time.unscaledTime - _lastFailLogTime > 1f)
				{
					_lastFailLogTime = Time.unscaledTime;
					DebugConsole.LogWarning($"[Registry] Lookup failed (#{_lookupFailCount}): NetId {netId} not found. Count: {identities.Count}");
				}
			}
			
			if (entity.IsNullOrDestroyed() || entity.gameObject.IsNullOrDestroyed())
			{
				identities.Remove(netId);
				entity = null;
				return false;
			}
			
			return found;
		}

		private static bool ReleaseUnavailableClientRegistration(int netId)
		{
			if (!MultiplayerSession.IsClient)
				return false;
			return ReleaseUnavailableRegistration(netId);
		}

		public static bool TryGetComponent<T>(int netId, out T component)
		{
			using var _ = Profiler.Scope();

			component = default(T);
			if (!TryGet(netId, out var ni))
				return false;
			if(ni.gameObject.IsNullOrDestroyed())
				return false;
			return ni.gameObject.TryGetComponent<T>(out component);
		}
		public static bool TryGetComponent<T>(NetworkIdentity ni, out T component)
		{
			using var _ = Profiler.Scope();

			component = default(T);
			if (ni.IsNullOrDestroyed() || ni.gameObject.IsNullOrDestroyed())
				return false;
			return ni.gameObject.TryGetComponent<T>(out component);
		}

		public static void Clear()
		{
			using var _ = Profiler.Scope();

				identities.Clear();
				unassigned.Clear();
				lifecycleRevisions.Clear();
				lifecycleTombstones.Clear();
				storageSnapshotRevisions.Clear();
				storageEventRevisions.Clear();
				storageItemRevisions.Clear();
				stateRevisions.Clear();
				nextAllocatedNetId = 1;
				nextAuthorityRevision = 0;
				lifecyclePruneFrozen = false;
				ReliableSyncBacklog.ClearAll();
			_lookupFailCount = 0;
			// TODO Rope into 1
			GroundItemPickedUpPacket.ClearPending();
			StorageItemPacket.ClearPending();
			SpawnPrefabPacket.ClearState();
			WorldCyclePacket.ClearState();

			PlayAnimPacket.ClearState();
		}

		public static IEnumerable<NetworkIdentity> AllIdentities => identities.Values;
	}
}
