using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.OxySync.Packets;
using ONI_Together.Networking.Packets.DuplicantActions;
using Shared.Helpers;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components
{
    public class OxySyncManager : MonoBehaviour
    {
        public static OxySyncManager? Instance { get; private set; }

        private readonly List<NetworkBehaviour> _behaviours = new();
		private readonly List<NetworkTransform> _realtimeTransforms = new();
		private readonly List<OxySyncEntityPositionHandler> _duplicantTransforms = new();
		private readonly Dictionary<int, List<DuplicantVisualSnapshot>> _duplicantSnapshotsByGroup = new();
        private readonly Dictionary<(int Group, PacketSendMode Mode), List<(int Hash, Variant Value)>> _changedByGroup = new();
        private readonly HashSet<Type> _explicitGroupTypes = new();
        private readonly Dictionary<int, HashSet<NetworkBehaviour>> _behavioursByGroup = new();

        private float _tickAccumulator;
		private int _syncCursor;

		// Keep network serialization from monopolizing a frame. At triple speed the
		// game simulation already has substantially more work to do, so use a
		// smaller slice and continue from the previous position on the next tick.
		private const int MaxBehavioursPerTick = 256;
		private const double NormalSyncBudgetMilliseconds = 4.0;
		private const double TripleSpeedSyncBudgetMilliseconds = 2.0;

        public int RegisteredCount => _behaviours.Count;
        public IReadOnlyList<NetworkBehaviour> AllBehaviours => _behaviours;
        public static int GetBehaviourCountInGroup(int groupId) =>
            Instance != null && Instance._behavioursByGroup.TryGetValue(groupId, out var set) ? set.Count : 0;

        private void Awake()
        {
            Instance = this;

            NetworkBehaviour.OnSpawned += Register;
            NetworkBehaviour.OnBehaviourCleanUp += Unregister;

            NetworkBehaviour.NetIdQuery = (behaviour) => behaviour.GetComponent<NetworkIdentity>()?.NetId ?? 0;

            NetworkBehaviour.NetIdSetter = (behaviour, newNetId) => behaviour.gameObject.AddOrGet<NetworkIdentity>().OverrideNetId(newNetId);

            NetIdentityHelper.SetIdentity = (go, netId) =>
            {
                var identity = go.AddOrGet<NetworkIdentity>();
                if (netId != 0)
                    identity.OverrideNetId(netId);
                else if (identity.NetId == 0)
                    identity.RegisterIdentity();
                return identity.NetId;
            };

            NetIdentityHelper.OverrideIdentity = (go, netId) =>
            {
                var identity = go.AddOrGet<NetworkIdentity>();
                identity.OverrideNetId(netId);
                return identity.NetId;
            };

            NetworkBehaviour.LogWarning = (msg) => DebugConsole.LogWarning(msg);

            NetworkBehaviour.IsHostQuery = () => MultiplayerSession.IsHost;
            NetworkBehaviour.IsClientQuery = () => MultiplayerSession.IsClient;
            NetworkBehaviour.InSessionQuery = () => MultiplayerSession.InActiveSession;

            NetworkBehaviour.SendCommandToHost = (netId, methodHash, args, sendType) =>
            {
                PacketSender.SendToHost(new CommandPacket
                {
                    NetId = netId,
                    MethodHash = methodHash,
                    Args = args,
                }, (PacketSendMode)sendType);
                return true;
            };

            NetworkBehaviour.SendClientRpcToAll = (netId, methodHash, args, sendType) =>
            {
                PacketSender.SendToAllClients(new ClientRpcPacket
                {
                    NetId = netId,
                    MethodHash = methodHash,
                    Args = args,
                    TargetPlayerId = ulong.MaxValue,
                }, (PacketSendMode)sendType);
                return true;
            };

            NetworkBehaviour.SendClientRpcToGroup = (group, netId, methodHash, args, sendType) =>
            {
                PacketSender.SendToGroup(group, new ClientRpcPacket
                {
                    NetId = netId,
                    MethodHash = methodHash,
                    Args = args,
                    TargetPlayerId = ulong.MaxValue,
                }, (PacketSendMode)sendType);
                return true;
            };

            NetworkBehaviour.LocalUserIdQuery = () => MultiplayerSession.LocalUserID;

            NetworkBehaviour.SendTargetRpcToPlayer = (targetPlayer, netId, methodHash, args, sendType) =>
            {
                PacketSender.SendToPlayer(targetPlayer, new ClientRpcPacket
                {
                    NetId = netId,
                    MethodHash = methodHash,
                    Args = args,
                    TargetPlayerId = targetPlayer,
                }, (PacketSendMode)sendType);
                return true;
            };
        }

        private void OnDestroy()
        {
            NetworkBehaviour.OnSpawned -= Register;
            NetworkBehaviour.OnBehaviourCleanUp -= Unregister;

            if (Instance == this)
            {
                Instance = null;

                NetworkBehaviour.NetIdQuery = null;
                NetworkBehaviour.NetIdSetter = null;
                NetworkBehaviour.LogWarning = null;
                NetworkBehaviour.IsHostQuery = null;
                NetworkBehaviour.IsClientQuery = null;
                NetworkBehaviour.InSessionQuery = null;
                NetworkBehaviour.SendCommandToHost = null;
                NetworkBehaviour.SendClientRpcToAll = null;
                NetworkBehaviour.SendClientRpcToGroup = null;
                NetworkBehaviour.SendTargetRpcToPlayer = null;
                NetworkBehaviour.LocalUserIdQuery = null;
            }

            _behaviours.Clear();
            _realtimeTransforms.Clear();
			_duplicantTransforms.Clear();
			_duplicantSnapshotsByGroup.Clear();
            _behavioursByGroup.Clear();
            _explicitGroupTypes.Clear();
        }

		private void Register(NetworkBehaviour behaviour)
		{
			if (!_behaviours.Contains(behaviour))
				_behaviours.Add(behaviour);
			if (behaviour is NetworkTransform transform && !_realtimeTransforms.Contains(transform))
				_realtimeTransforms.Add(transform);
			if (behaviour is OxySyncEntityPositionHandler entityTransform
				&& entityTransform.IsDuplicant
				&& !_duplicantTransforms.Contains(entityTransform))
			{
				_duplicantTransforms.Add(entityTransform);
			}

			if (behaviour.GetType().GetCustomAttribute<FixedInterestGroupAttribute>() != null)
				_explicitGroupTypes.Add(behaviour.GetType());

			if (behaviour.InterestGroup == -1 && !_explicitGroupTypes.Contains(behaviour.GetType()))
			{
				int worldId = behaviour.GetMyWorldId();
				if (worldId >= 0)
					behaviour.InterestGroup = WorldChunkHelper.GetGroupId(worldId,
						Grid.PosToCell(behaviour.transform.position));
			}

			IndexBehaviour(behaviour);
		}

        private void Unregister(NetworkBehaviour behaviour)
        {
            _behaviours.Remove(behaviour);
			if (behaviour is NetworkTransform transform)
				_realtimeTransforms.Remove(transform);
			if (behaviour is OxySyncEntityPositionHandler entityTransform)
				_duplicantTransforms.Remove(entityTransform);

            RemoveBehaviourFromGroupIndex(behaviour, behaviour.InterestGroup);
            var fields = behaviour.SyncVarFields;
            for (int i = 0; i < fields.Count; i++)
            {
                int g = fields[i].InterestGroup;
                if (g != -1)
                    RemoveBehaviourFromGroupIndex(behaviour, g);
            }
        }

        private void Update()
        {
            if (!MultiplayerSession.IsHost) return;
            if (_behaviours.Count == 0) return;

            _tickAccumulator += Time.unscaledDeltaTime;
            _tickAccumulator = Mathf.Min(_tickAccumulator, GameServer.TickInterval * GameServer.MaxMissedTicks);
            if (_tickAccumulator < GameServer.TickInterval)
                return;
            _tickAccumulator -= GameServer.TickInterval;

            var sw = Stopwatch.StartNew();
            int totalChanges = 0;

			// Duplicants share one compact packet timestamp per interest group.
			// Their navigation-aware client controller is the only transform writer,
			// so they must not also receive independent SyncVar transform packets.
			ProcessDuplicantTransformBatches(ref totalChanges);

			// Moving entities need a stable 20 Hz stream for snapshot interpolation.
			// Process them before the general budget so status/building work can never
			// make duplicants or critters run out of interpolation snapshots.
			for (int i = _realtimeTransforms.Count - 1; i >= 0; i--)
			{
				var transform = _realtimeTransforms[i];
				if (transform.IsNullOrDestroyed())
				{
					_realtimeTransforms.RemoveAt(i);
					continue;
				}
				if (transform is OxySyncEntityPositionHandler entityTransform && entityTransform.IsDuplicant)
					continue;

				ProcessBehaviour(transform, ref totalChanges);
			}

            int behavioursAtStart = _behaviours.Count;
			int visited = 0;
			double workBudget = GetSyncWorkBudgetMilliseconds(
				SpeedControlScreen.Instance != null ? SpeedControlScreen.Instance.GetSpeed() : 0);

			while (_behaviours.Count > 0
				&& visited < behavioursAtStart
				&& visited < MaxBehavioursPerTick
				&& sw.Elapsed.TotalMilliseconds < workBudget)
            {
				if (_syncCursor >= _behaviours.Count)
					_syncCursor = 0;

				var behaviour = _behaviours[_syncCursor];
                if (behaviour.IsNullOrDestroyed())
                {
					_behaviours.RemoveAt(_syncCursor);
					behavioursAtStart--;
                    continue;
                }

				_syncCursor++;
				visited++;

				if (behaviour is NetworkTransform)
					continue;

				ProcessBehaviour(behaviour, ref totalChanges);
            }

            if (totalChanges > 0)
            {
                sw.Stop();
                SyncStats.RecordSync(SyncStats.OxySync, totalChanges, totalChanges * 16, sw.ElapsedMilliseconds);
            }
        }

		private void ProcessDuplicantTransformBatches(ref int totalChanges)
		{
			_duplicantSnapshotsByGroup.Clear();
			long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

			for (int i = _duplicantTransforms.Count - 1; i >= 0; i--)
			{
				var transform = _duplicantTransforms[i];
				if (transform.IsNullOrDestroyed())
				{
					_duplicantTransforms.RemoveAt(i);
					continue;
				}

				if (Time.unscaledTime - transform._lastSyncTime < transform.SyncInterval)
					continue;

				transform._lastSyncTime = Time.unscaledTime;
				RefreshDuplicantInterestGroup(transform);

				if (!transform.TryCaptureVisualSnapshot(false, false, out var snapshot))
					continue;

				int group = transform.InterestGroup;
				if (!_duplicantSnapshotsByGroup.TryGetValue(group, out var snapshots))
				{
					snapshots = new List<DuplicantVisualSnapshot>();
					_duplicantSnapshotsByGroup[group] = snapshots;
				}

				snapshots.Add(snapshot);
				if (InterestGroupManager.GetPlayersInGroup(group).Count > 0)
					transform._lastActiveSyncTime = Time.unscaledTime;
			}

			foreach (var groupSnapshots in _duplicantSnapshotsByGroup)
			{
				var snapshots = groupSnapshots.Value;
				for (int offset = 0; offset < snapshots.Count; offset += DuplicantVisualSnapshotBatchPacket.MaxEntries)
				{
					int count = Math.Min(DuplicantVisualSnapshotBatchPacket.MaxEntries, snapshots.Count - offset);
					var packet = new DuplicantVisualSnapshotBatchPacket
					{
						ServerTimestamp = timestamp,
						Snapshots = new List<DuplicantVisualSnapshot>(count),
					};
					for (int i = 0; i < count; i++)
						packet.Snapshots.Add(snapshots[offset + i]);

					PacketSender.SendToGroup(groupSnapshots.Key, packet, PacketSendMode.UnreliableNoDelay);
					totalChanges += count;
				}
			}
		}

		private void RefreshDuplicantInterestGroup(OxySyncEntityPositionHandler transform)
		{
			if (_explicitGroupTypes.Contains(transform.GetType()))
				return;

			int worldId = transform.GetMyWorldId();
			if (worldId < 0)
				return;

			int newGroup = WorldChunkHelper.GetGroupId(worldId, Grid.PosToCell(transform.transform.position));
			if (newGroup == transform.InterestGroup)
				return;

			RemoveBehaviourFromGroupIndex(transform, transform.InterestGroup);
			transform.InterestGroup = newGroup;
			AddBehaviourToGroupIndex(transform, newGroup);
		}

		private void ProcessBehaviour(NetworkBehaviour behaviour, ref int totalChanges)
		{
			if (Time.unscaledTime - behaviour._lastSyncTime < behaviour.SyncInterval)
				return;

			behaviour._lastSyncTime = Time.unscaledTime;
			var manualDirty = behaviour.GetAndClearDirtyIndices();
			_changedByGroup.Clear();
			var fields = behaviour.SyncVarFields;

			for (int j = 0; j < fields.Count; j++)
			{
				var field = fields[j];
				bool isManuallyDirty = manualDirty?.Contains(j) == true;
				Variant currentVariant;
				if (isManuallyDirty)
				{
					currentVariant = VariantHelper.ObjectToVariant(field.Info.GetValue(behaviour));
				}
				else
				{
					currentVariant = VariantHelper.ObjectToVariant(field.Info.GetValue(behaviour));
					var lastVariant = VariantHelper.ObjectToVariant(field.LastSentValue);
					if (!VariantHelper.ValuesDiffer(currentVariant, lastVariant, field.Epsilon))
						continue;
				}

				int group = field.InterestGroup == -1 ? behaviour.InterestGroup : field.InterestGroup;
				var sendMode = (PacketSendMode)field.SendMode;
				if (behaviour is NetworkTransform && sendMode == PacketSendMode.Unreliable)
					sendMode = PacketSendMode.UnreliableNoDelay;
				var key = (group, sendMode);
				if (!_changedByGroup.TryGetValue(key, out var list))
				{
					list = new List<(int Hash, Variant Value)>();
					_changedByGroup[key] = list;
				}
				list.Add((field.Hash, currentVariant));
			}

			if (_changedByGroup.Count == 0)
				return;

			var identity = behaviour.GetComponent<NetworkIdentity>();
			if (identity == null || identity.NetId == 0)
				return;

			int netId = identity.NetId;
			long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			foreach (var kvp in _changedByGroup)
			{
				int groupId = kvp.Key.Group;
				var sendMode = kvp.Key.Mode;
				var updates = kvp.Value;
				totalChanges += updates.Count;
				if (updates.Count == 1)
				{
					var update = updates[0];
					PacketSender.SendToGroup(groupId, new SyncVarPacket
					{
						NetId = netId,
						FieldHash = update.Hash,
						Value = update.Value,
						Timestamp = timestamp,
					}, sendMode);
				}
				else
				{
					PacketSender.SendToGroup(groupId, new SyncVarBatchPacket(netId, updates)
					{
						Timestamp = timestamp,
					}, sendMode);
				}
			}

			foreach (var key in _changedByGroup.Keys)
			{
				if (InterestGroupManager.GetPlayersInGroup(key.Group).Count > 0)
				{
					behaviour._lastActiveSyncTime = Time.unscaledTime;
					break;
				}
			}

			behaviour.SyncLastSentValues();
			if (_explicitGroupTypes.Contains(behaviour.GetType()))
				return;

			int currentWorld = behaviour.GetMyWorldId();
			if (currentWorld < 0)
				return;

			int newGroup = WorldChunkHelper.GetGroupId(currentWorld,
				Grid.PosToCell(behaviour.transform.position));
			if (newGroup == behaviour.InterestGroup)
				return;

			RemoveBehaviourFromGroupIndex(behaviour, behaviour.InterestGroup);
			behaviour.InterestGroup = newGroup;
			AddBehaviourToGroupIndex(behaviour, newGroup);
			behaviour.MarkAllDirty();
		}

		internal static double GetSyncWorkBudgetMilliseconds(int gameSpeed)
		{
			return gameSpeed >= 2
				? TripleSpeedSyncBudgetMilliseconds
				: NormalSyncBudgetMilliseconds;
		}

        private void IndexBehaviour(NetworkBehaviour behaviour)
        {
            var fields = behaviour.SyncVarFields;
            var grouped = new HashSet<int>();

            int primaryGroup = behaviour.InterestGroup;
            if (primaryGroup != -1 && grouped.Add(primaryGroup))
                AddBehaviourToGroupIndex(behaviour, primaryGroup);

            for (int i = 0; i < fields.Count; i++)
            {
                int g = fields[i].InterestGroup;
                if (g == -1) continue;
                if (grouped.Add(g))
                    AddBehaviourToGroupIndex(behaviour, g);
            }
        }

        private void AddBehaviourToGroupIndex(NetworkBehaviour behaviour, int groupId)
        {
            if (!_behavioursByGroup.TryGetValue(groupId, out var set))
            {
                set = new HashSet<NetworkBehaviour>();
                _behavioursByGroup[groupId] = set;
            }
            set.Add(behaviour);
        }

        private void RemoveBehaviourFromGroupIndex(NetworkBehaviour behaviour, int groupId)
        {
            if (_behavioursByGroup.TryGetValue(groupId, out var set))
            {
                set.Remove(behaviour);
                if (set.Count == 0)
                    _behavioursByGroup.Remove(groupId);
            }
        }

        public static void SendFullStateToPlayerForGroup(ulong playerId, int groupId)
        {
            if (Instance == null) return;
            if (!MultiplayerSession.IsHost) return;

            if (!Instance._behavioursByGroup.TryGetValue(groupId, out var behavioursInGroup))
                return;

            foreach (var behaviour in behavioursInGroup)
            {
                if (behaviour.IsNullOrDestroyed()) continue;

                int netId = behaviour.NetId;
                if (netId == 0) continue;

				if (behaviour is OxySyncEntityPositionHandler duplicantTransform
					&& duplicantTransform.IsDuplicant
					&& duplicantTransform.TryCaptureVisualSnapshot(true, true, out var visualSnapshot))
				{
					PacketSender.SendToPlayer(playerId, new DuplicantVisualSnapshotBatchPacket
					{
						ServerTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
						Snapshots = new List<DuplicantVisualSnapshot> { visualSnapshot },
					}, PacketSendMode.ReliableImmediate);
					continue;
				}

                var fields = behaviour.SyncVarFields;
                if (fields.Count == 0) continue;

                var updates = new List<(int Hash, Variant Value)>();
                for (int i = 0; i < fields.Count; i++)
                {
                    var field = fields[i];
                    int fieldGroup = field.InterestGroup;
                    if (fieldGroup == -1) fieldGroup = behaviour.InterestGroup;
                    if (fieldGroup != groupId) continue;

                    updates.Add((field.Hash, VariantHelper.ObjectToVariant(field.Info.GetValue(behaviour))));
                }

                if (updates.Count == 0) continue;

                long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                if (updates.Count == 1)
                {
                    var update = updates[0];
                    PacketSender.SendToPlayer(playerId, new SyncVarPacket
                    {
                        NetId = netId,
                        FieldHash = update.Hash,
                        Value = update.Value,
                        Timestamp = timestamp,
                    }, PacketSendMode.ReliableImmediate);
                }
                else
                {
                    PacketSender.SendToPlayer(playerId, new SyncVarBatchPacket(netId, updates)
                    {
                        Timestamp = timestamp,
                    }, PacketSendMode.ReliableImmediate);
                }
            }
        }
    }
}
