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

        private readonly List<ISyncBehaviour> _behaviours = new();
        private readonly Dictionary<(int Group, PacketSendMode Mode), List<(int Hash, Variant Value)>> _changedByGroup = new();
        private readonly HashSet<Type> _explicitGroupTypes = new();
        private readonly Dictionary<int, HashSet<ISyncBehaviour>> _behavioursByGroup = new();

        private readonly Dictionary<(int, int), ISyncBehaviour> _behaviourLookup = new();
        private readonly Dictionary<(int NetId, int TypeHash), int> _typeOrdinals = new();

        private float _tickAccumulator;

        public int RegisteredCount => _behaviours.Count;
        public IReadOnlyList<ISyncBehaviour> AllBehaviours => _behaviours;

        /// <summary>
        /// Native (ONI Together) behaviours only, for tooling that needs the typed
        /// <see cref="Shared.OxySync.NetworkBehaviour"/> surface (e.g. the debug inspector).
        /// </summary>
        public IReadOnlyList<NetworkBehaviour> NativeBehaviours
        {
            get
            {
                var result = new List<NetworkBehaviour>(_behaviours.Count);
                for (int i = 0; i < _behaviours.Count; i++)
                {
                    if (_behaviours[i] is NativeSyncBehaviour native && !native.IsDestroyed)
                        result.Add(native.Native);
                }
                return result;
            }
        }

        /// <summary>
        /// Behaviours that came from the ONI_Together_API assembly, exposed for the debug inspector.
        /// </summary>
        public IReadOnlyList<ForeignSyncBehaviour> ForeignBehaviours
        {
            get
            {
                var result = new List<ForeignSyncBehaviour>(_behaviours.Count);
                for (int i = 0; i < _behaviours.Count; i++)
                {
                    if (_behaviours[i] is ForeignSyncBehaviour foreign && !foreign.IsDestroyed)
                        result.Add(foreign);
                }
                return result;
            }
        }

        /// <summary>
        /// Adds or fetches a <see cref="NetworkIdentity"/> on a GameObject and resolves its NetId,
        /// preferring a supplied override. Shared by the native OxySync path and the API bridge.
        /// </summary>
        public static int SetOrGetIdentity(GameObject go, int netId)
        {
            var identity = go.AddOrGet<NetworkIdentity>();
            if (netId != 0)
                identity.OverrideNetId(netId);
            else if (identity.NetId == 0)
                identity.RegisterIdentity();
            return identity.NetId;
        }

        /// <summary>
        /// Adds a <see cref="NetworkIdentity"/> to a GameObject and registers it if it has no NetId,
        /// returning the resolved NetId. Equivalent to <see cref="SetOrGetIdentity"/> with no override.
        /// </summary>
        public static int AddIdentity(GameObject go)
        {
            var identity = go.AddOrGet<NetworkIdentity>();
            if (identity.NetId == 0)
                identity.RegisterIdentity();
            return identity.NetId;
        }

        /// <summary>
        /// Reads the NetId of an existing <see cref="NetworkIdentity"/> on a GameObject without
        /// creating or registering one. Returns 0 when none exists.
        /// </summary>
        public static int GetIdentity(GameObject go)
        {
            if (go == null)
                return 0;

            return go.TryGetComponent<NetworkIdentity>(out var identity) && identity != null
                ? identity.NetId
                : 0;
        }

        /// <summary>
        /// Forces a <see cref="NetworkIdentity"/> on a GameObject to a specific NetId.
        /// </summary>
        public static int OverrideIdentity(GameObject go, int netId)
        {
            var identity = go.AddOrGet<NetworkIdentity>();
            identity.OverrideNetId(netId);
            return identity.NetId;
        }

        public static bool TryGetBehaviour(int NetId, int BehaviourId, out NetworkBehaviour behaviour)
        {
            behaviour = null;
            if (Instance == null)
                return false;

            if (Instance._behaviourLookup.TryGetValue((NetId, BehaviourId), out var syncBehaviour)
                && syncBehaviour is NativeSyncBehaviour native)
            {
                behaviour = native.Native;
                return true;
            }
            return false;
        }

        public static bool TryGetSyncBehaviour(int NetId, int BehaviourId, out ISyncBehaviour behaviour)
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

            NetIdentityHelper.SetIdentity = SetOrGetIdentity;
            NetIdentityHelper.AddIdentity = AddIdentity;
            NetIdentityHelper.GetIdentity = GetIdentity;
            NetIdentityHelper.OverrideIdentity = OverrideIdentity;

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
            if (behaviour == null) return;
            RegisterSyncBehaviour(new NativeSyncBehaviour(behaviour));
		}

        /// <summary>
        /// Registers an already-adapted behaviour. Used by the native path (via <see cref="Register"/>)
        /// and by <c>OxySync_API_Helper</c> for behaviours loaded from the ONI Together API assembly.
        /// </summary>
        public void RegisterSyncBehaviour(ISyncBehaviour behaviour)
        {
            if (behaviour == null) return;

            if (!_behaviours.Contains(behaviour))
                _behaviours.Add(behaviour);

            behaviour.RefreshSyncVars();

            ResolveBehaviourId(behaviour);
            _behaviourLookup[(behaviour.NetId, behaviour.BehaviourId)] = behaviour;

            var behaviourType = behaviour.UnderlyingType;
            if (behaviourType.GetCustomAttribute<FixedInterestGroupAttribute>() != null)
                _explicitGroupTypes.Add(behaviourType);

            if (behaviour.InterestGroup == -1 && !_explicitGroupTypes.Contains(behaviourType))
            {
                int worldId = behaviour.GetMyWorldId();
                if (worldId >= 0 && behaviour.GameObject != null)
                    behaviour.InterestGroup = WorldChunkHelper.GetGroupId(worldId,
                        Grid.PosToCell(behaviour.GameObject.transform.position));
            }

            IndexBehaviour(behaviour);
        }

        private void Unregister(NetworkBehaviour behaviour)
        {
            if (behaviour == null) return;
            if (Instance == null) return;

            for (int i = 0; i < _behaviours.Count; i++)
            {
                if (_behaviours[i] is NativeSyncBehaviour native && native.Native == behaviour)
                {
                    UnregisterSyncBehaviour(_behaviours[i]);
                    return;
                }
            }
        }

        /// <summary>
        /// Unregisters an adapted behaviour. Used by the native path and the API bridge.
        /// </summary>
        public void UnregisterSyncBehaviour(ISyncBehaviour behaviour)
        {
            if (behaviour == null) return;

            _behaviours.Remove(behaviour);

            _behaviourLookup.Remove((behaviour.NetId, behaviour.BehaviourId));

            RemoveBehaviourFromGroupIndex(behaviour, behaviour.InterestGroup);
            behaviour.RefreshSyncVars();
            int fieldCount = behaviour.SyncVarCount;
            for (int i = 0; i < fieldCount; i++)
            {
                int g = behaviour.GetSyncVar(i).InterestGroup;
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
                if (behaviour.IsDestroyed)
                {
                    _behaviours.RemoveAt(i);
                    continue;
                }

                if (Time.unscaledTime - behaviour.LastSyncTime < behaviour.SyncInterval)
                    continue;

                behaviour.LastSyncTime = Time.unscaledTime;
                behaviour.RefreshSyncVars();

                ulong manualDirty = behaviour.GetAndClearDirtyBits();

                _changedByGroup.Clear();
                CollectChanges(behaviour, manualDirty, _changedByGroup);

                if (_changedByGroup.Count == 0) continue;

                var identity = behaviour.GameObject != null
                    ? behaviour.GameObject.GetComponent<NetworkIdentity>()
                    : null;
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
                    behaviour.LastActiveSyncTime = Time.unscaledTime;

                behaviour.SyncLastSentValues();

				if (!_explicitGroupTypes.Contains(behaviour.UnderlyingType))
				{
					int currentWorld = behaviour.GetMyWorldId();
					if (currentWorld >= 0 && behaviour.GameObject != null)
					{
						int newGroup = WorldChunkHelper.GetGroupId(currentWorld, Grid.PosToCell(behaviour.GameObject.transform.position));
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
            CollectChanges(new NativeSyncBehaviour(behaviour), manualDirty, changes);
        }

        internal static void CollectChanges(ISyncBehaviour behaviour, ulong manualDirty, Dictionary<(int Group, PacketSendMode Mode), List<(int Hash, Variant Value)>> changes)
        {
            int fieldCount = behaviour.SyncVarCount;

            ulong remaining = manualDirty;
            while (remaining != 0)
            {
                int index = BitUtils.TrailingZeroCount(remaining);
                remaining &= remaining - 1;

                if (index >= fieldCount) continue;

                var field = behaviour.GetSyncVar(index);
                AddChange(changes, behaviour, field, VariantHelper.ObjectToVariant(field.GetValue()));
            }

            for (int j = 0; j < fieldCount; j++)
            {
                if ((manualDirty & (1UL << j)) != 0) continue;

                var field = behaviour.GetSyncVar(j);
                var currentValue = field.GetValue();
                var currentVariant = VariantHelper.ObjectToVariant(currentValue);
                var lastVariant = VariantHelper.ObjectToVariant(field.LastSentValue);
                if (!VariantHelper.ValuesDiffer(currentVariant, lastVariant, field.Epsilon))
                    continue;

                AddChange(changes, behaviour, field, currentVariant);
            }
        }

        private static void AddChange(Dictionary<(int Group, PacketSendMode Mode), List<(int Hash, Variant Value)>> changes, ISyncBehaviour behaviour, SyncVarDescriptor field, Variant value)
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

        private void IndexBehaviour(ISyncBehaviour behaviour)
        {
            var grouped = new HashSet<int>();

            int primaryGroup = behaviour.InterestGroup;
            if (primaryGroup != -1 && grouped.Add(primaryGroup))
                AddBehaviourToGroupIndex(behaviour, primaryGroup);

            int fieldCount = behaviour.SyncVarCount;
            for (int i = 0; i < fieldCount; i++)
            {
                int g = behaviour.GetSyncVar(i).InterestGroup;
                if (g == -1) continue;
                if (grouped.Add(g))
                    AddBehaviourToGroupIndex(behaviour, g);
            }
        }

        private void AddBehaviourToGroupIndex(ISyncBehaviour behaviour, int groupId)
        {
            if (!_behavioursByGroup.TryGetValue(groupId, out var set))
            {
                set = new HashSet<ISyncBehaviour>();
                _behavioursByGroup[groupId] = set;
            }
            set.Add(behaviour);
        }

        private void RemoveBehaviourFromGroupIndex(ISyncBehaviour behaviour, int groupId)
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
                if (behaviour.IsDestroyed) continue;

                int netId = behaviour.NetId;
                if (netId == 0) continue;
                int behaviourId = behaviour.BehaviourId;

                behaviour.RefreshSyncVars();
                int fieldCount = behaviour.SyncVarCount;
                if (fieldCount == 0) continue;

                var updates = new List<(int Hash, Variant Value)>();
                for (int i = 0; i < fieldCount; i++)
                {
                    var field = behaviour.GetSyncVar(i);
                    int fieldGroup = field.InterestGroup;
                    if (fieldGroup == -1) fieldGroup = behaviour.InterestGroup;
                    if (fieldGroup != groupId) continue;

                    updates.Add((field.Hash, VariantHelper.ObjectToVariant(field.GetValue())));
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

        private void ResolveBehaviourId(ISyncBehaviour behaviour)
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