using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.OxySync.Packets;
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
        private readonly Dictionary<(int Group, PacketSendMode Mode), List<(int Hash, Variant Value)>> _changedByGroup = new();
        private readonly HashSet<Type> _explicitGroupTypes = new();
        private readonly Dictionary<int, HashSet<NetworkBehaviour>> _behavioursByGroup = new();

        private float _tickAccumulator;

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
                Instance = null;
        }

		private void Register(NetworkBehaviour behaviour)
		{
			if (!_behaviours.Contains(behaviour))
				_behaviours.Add(behaviour);

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

            for (int i = _behaviours.Count - 1; i >= 0; i--)
            {
                var behaviour = _behaviours[i];
                if (behaviour.IsNullOrDestroyed())
                {
                    _behaviours.RemoveAt(i);
                    continue;
                }

                if (Time.unscaledTime - behaviour._lastSyncTime < behaviour.SyncInterval)
                    continue;

                behaviour._lastSyncTime = Time.unscaledTime;

                uint manualDirty = behaviour.GetAndClearDirtyBits();

                _changedByGroup.Clear();
                var fields = behaviour.SyncVarFields;

                for (int j = 0; j < fields.Count; j++)
                {
                    var field = fields[j];
                    bool isManuallyDirty = (manualDirty & (1u << j)) != 0;

                    Variant currentVariant;
                    if (isManuallyDirty)
                    {
                        currentVariant = ObjectToVariant(field.Info.GetValue(behaviour));
                    }
                    else
                    {
                        var currentValue = field.Info.GetValue(behaviour);
                        currentVariant = ObjectToVariant(currentValue);
                        var lastVariant = ObjectToVariant(field.LastSentValue);
                        if (!ValuesDiffer(currentVariant, lastVariant, field.Epsilon))
                            continue;
                    }

                    int group = field.InterestGroup;
                    if (group == -1) group = behaviour.InterestGroup;
                    var key = (group, (PacketSendMode)field.SendMode);
                    if (!_changedByGroup.TryGetValue(key, out var list))
                    {
                        list = new List<(int Hash, Variant Value)>();
                        _changedByGroup[key] = list;
                    }
                    list.Add((field.Hash, currentVariant));
                }

                if (_changedByGroup.Count == 0) continue;

                var identity = behaviour.GetComponent<NetworkIdentity>();
                if (identity == null || identity.NetId == 0)
                    continue;

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
                        var batch = new SyncVarBatchPacket(netId, updates)
                        {
                            Timestamp = timestamp,
                        };
                        PacketSender.SendToGroup(groupId, batch, sendMode);
                    }
                }

                bool hasSubscribers = false;
                foreach (var key in _changedByGroup.Keys)
                {
                    if (InterestGroupManager.GetPlayersInGroup(key.Group).Count > 0)
                    {
                        hasSubscribers = true;
                        break;
                    }
                }

                if (hasSubscribers)
                    behaviour._lastActiveSyncTime = Time.unscaledTime;

                behaviour.SyncLastSentValues();

				if (!_explicitGroupTypes.Contains(behaviour.GetType()))
				{
					int currentWorld = behaviour.GetMyWorldId();
					if (currentWorld >= 0)
					{
						int newGroup = WorldChunkHelper.GetGroupId(currentWorld,
							Grid.PosToCell(behaviour.transform.position));
						if (newGroup != behaviour.InterestGroup)
							{
								RemoveBehaviourFromGroupIndex(behaviour, behaviour.InterestGroup);
								behaviour.InterestGroup = newGroup;
								AddBehaviourToGroupIndex(behaviour, newGroup);
								behaviour.MarkAllDirty();
							}
					}
				}
            }

            if (totalChanges > 0)
            {
                sw.Stop();
                SyncStats.RecordSync(SyncStats.OxySync, totalChanges, totalChanges * 16, sw.ElapsedMilliseconds);
            }
        }

        private static Variant ObjectToVariant(object? value)
        {
            if (value is int i) return i;
            if (value is float f) return f;
            if (value is byte b) return b;
            if (value is string s) return (Variant)s;
            if (value is bool bv) return bv;
            if (value is Vector3 v3) return v3;
            if (value is Vector2 v2) return v2;
            if (value is byte[] ba) return ba;
            if (value is Quaternion q) return q;
            if (value is HashedString hs) return hs;
            if (value is KAnimHashedString khs) return khs;
            return 0;
        }

        private static bool ValuesDiffer(Variant a, Variant b, float epsilon)
        {
            if (a.Type != b.Type) return true;
            return a.Type switch
            {
                Variant.TypeCode.Float => Mathf.Abs(a.Float - b.Float) > epsilon,
                Variant.TypeCode.Int => a.Int != b.Int,
                Variant.TypeCode.Byte => a.Byte != b.Byte,
                Variant.TypeCode.String => a.String != b.String,
                Variant.TypeCode.Boolean => a.Boolean != b.Boolean,
                Variant.TypeCode.Vector3 => Vector3.Distance(a.Vector3, b.Vector3) > epsilon,
                Variant.TypeCode.Vector2 => Vector2.Distance(a.Vector2, b.Vector2) > epsilon,
                Variant.TypeCode.ByteArray => !ByteArraysEqual(a.ByteArray, b.ByteArray),
                Variant.TypeCode.Quaternion => Quaternion.Angle(a.Quaternion, b.Quaternion) > epsilon,
                Variant.TypeCode.HashedString => a.Int != b.Int,
                Variant.TypeCode.KAnimHashedString => a.Int != b.Int,
                _ => true,
            };
        }

        private static bool ByteArraysEqual(byte[]? a, byte[]? b)
        {
            if (a == b) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
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

                var fields = behaviour.SyncVarFields;
                if (fields.Count == 0) continue;

                var updates = new List<(int Hash, Variant Value)>();
                for (int i = 0; i < fields.Count; i++)
                {
                    var field = fields[i];
                    int fieldGroup = field.InterestGroup;
                    if (fieldGroup == -1) fieldGroup = behaviour.InterestGroup;
                    if (fieldGroup != groupId) continue;

                    updates.Add((field.Hash, ObjectToVariant(field.Info.GetValue(behaviour))));
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
