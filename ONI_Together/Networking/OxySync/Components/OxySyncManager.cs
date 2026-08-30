using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.OxySync.Packets;
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
        private readonly Dictionary<(int Group, PacketSendMode Mode), List<(int Hash, Variant Value)>> _changedByGroup = new();
        private readonly HashSet<Type> _explicitGroupTypes = new();
        private readonly Dictionary<int, HashSet<NetworkBehaviour>> _behavioursByGroup = new();

        private readonly Dictionary<(int, int), NetworkBehaviour> _behaviourLookup = new();
        private readonly Dictionary<(int NetId, int TypeHash), int> _typeOrdinals = new();

        private float _tickAccumulator;

        public int RegisteredCount => _behaviours.Count;
        public IReadOnlyList<NetworkBehaviour> AllBehaviours => _behaviours;

        public static bool TryGetBehaviour(int NetId, int BehaviourId, out NetworkBehaviour behaviour)
        {
            if (Instance == null)
            {
                behaviour = null;
                return false;
            }

            return Instance._behaviourLookup.TryGetValue((NetId, BehaviourId), out behaviour);
        }

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

            NetworkBehaviour.SendCommandToHost = (netId, behaviourId, methodHash, args, sendType) =>
            {
                PacketSender.SendToHost(new CommandPacket
                {
                    NetId = netId,
                    BehaviourId = behaviourId,
                    MethodHash = methodHash,
                    Args = args,
                }, (PacketSendMode)sendType);
                return true;
            };

            NetworkBehaviour.SendClientRpcToAll = (netId, behaviourId, methodHash, args, sendType) =>
            {
                PacketSender.SendToAllClients(new ClientRpcPacket
                {
                    NetId = netId,
                    BehaviourId = behaviourId,
                    MethodHash = methodHash,
                    Args = args,
                    TargetPlayerId = ulong.MaxValue,
                }, (PacketSendMode)sendType);
                return true;
            };

            NetworkBehaviour.SendClientRpcToGroup = (group, netId, behaviourId, methodHash, args, sendType) =>
            {
                PacketSender.SendToGroup(group, new ClientRpcPacket
                {
                    NetId = netId,
                    BehaviourId = behaviourId,
                    MethodHash = methodHash,
                    Args = args,
                    TargetPlayerId = ulong.MaxValue,
                }, (PacketSendMode)sendType);
                return true;
            };

            NetworkBehaviour.LocalUserIdQuery = () => MultiplayerSession.LocalUserID;

            NetworkBehaviour.SendTargetRpcToPlayer = (targetPlayer, netId, behaviourId, methodHash, args, sendType) =>
            {
                PacketSender.SendToPlayer(targetPlayer, new ClientRpcPacket
                {
                    NetId = netId,
                    BehaviourId = behaviourId,
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

            ResolveBehaviourId(behaviour);
            _behaviourLookup[(behaviour.NetId, behaviour.BehaviourId)] = behaviour;

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

            _behaviourLookup.Remove((behaviour.NetId, behaviour.BehaviourId));

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

                ulong manualDirty = behaviour.GetAndClearDirtyBits();

                _changedByGroup.Clear();
                CollectChanges(behaviour, manualDirty, _changedByGroup);

                if (_changedByGroup.Count == 0) continue;

                var identity = behaviour.GetComponent<NetworkIdentity>();
                if (identity == null || identity.NetId == 0)
                    continue;

                int netId = identity.NetId;
                int behaviourId = behaviour.BehaviourId;
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
                            BehaviourId = behaviourId,
                            FieldHash = update.Hash,
                            Value = update.Value,
                            Timestamp = timestamp,
                        }, sendMode);
                    }
                    else
                    {
						var batch = new SyncVarBatchPacket(netId, behaviourId, updates)
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
						int newGroup = WorldChunkHelper.GetGroupId(currentWorld, Grid.PosToCell(behaviour.transform.position));
						if (newGroup != behaviour.InterestGroup)
                        {
                            RemoveBehaviourFromGroupIndex(behaviour, behaviour.InterestGroup);
                            behaviour.InterestGroup = newGroup;
                            AddBehaviourToGroupIndex(behaviour, newGroup);
                            behaviour.MarkAllDirty(); // Looking at this I'm not 100% sure I need this anymore but I'll leave it - Lyraedan
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

        internal static void CollectChanges(NetworkBehaviour behaviour, ulong manualDirty, Dictionary<(int Group, PacketSendMode Mode), List<(int Hash, Variant Value)>> changes)
        {
            var fields = behaviour.SyncVarFields;

            ulong remaining = manualDirty;
            while (remaining != 0)
            {
                int index = BitUtils.TrailingZeroCount(remaining);
                remaining &= remaining - 1;

                if (index >= fields.Count) continue;

                var field = fields[index];
                AddChange(changes, behaviour, field, VariantHelper.ObjectToVariant(field.Info.GetValue(behaviour)));
            }

            for (int j = 0; j < fields.Count; j++)
            {
                if ((manualDirty & (1UL << j)) != 0) continue;

                var field = fields[j];
                var currentValue = field.Info.GetValue(behaviour);
                var currentVariant = VariantHelper.ObjectToVariant(currentValue);
                var lastVariant = VariantHelper.ObjectToVariant(field.LastSentValue);
                if (!VariantHelper.ValuesDiffer(currentVariant, lastVariant, field.Epsilon))
                    continue;

                AddChange(changes, behaviour, field, currentVariant);
            }
        }

        private static void AddChange(Dictionary<(int Group, PacketSendMode Mode), List<(int Hash, Variant Value)>> changes, NetworkBehaviour behaviour, NetworkBehaviour.SyncVarField field, Variant value)
        {
            int group = field.InterestGroup;
            if (group == -1) group = behaviour.InterestGroup;
            var key = (group, (PacketSendMode)field.SendMode);
            if (!changes.TryGetValue(key, out var list))
            {
                list = new List<(int Hash, Variant Value)>();
                changes[key] = list;
            }
            list.Add((field.Hash, value));
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
                int behaviourId = behaviour.BehaviourId;

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
                        BehaviourId = behaviourId,
                        FieldHash = update.Hash,
                        Value = update.Value,
                        Timestamp = timestamp,
                    }, PacketSendMode.ReliableImmediate);
                }
                else
                {
					PacketSender.SendToPlayer(playerId, new SyncVarBatchPacket(netId, behaviourId, updates)
                    {
                        Timestamp = timestamp,
                    }, PacketSendMode.ReliableImmediate);
                }
            }
        }

        private void ResolveBehaviourId(NetworkBehaviour behaviour)
        {
            int netId = behaviour.NetId;
            int id = behaviour.BehaviourId;

            if (!_behaviourLookup.ContainsKey((netId, id)))
                return;

            do
            {
                id++;
            } while (_behaviourLookup.ContainsKey((netId, id)));
            
            behaviour.BehaviourId = id;
        }
    }
}