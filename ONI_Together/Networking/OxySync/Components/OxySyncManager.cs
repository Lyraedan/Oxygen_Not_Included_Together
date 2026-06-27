using System;
using System.Collections.Generic;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.OxySync.Packets;
using Shared.OxySync;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components
{
    public class OxySyncManager : MonoBehaviour
    {
        public static OxySyncManager? Instance { get; private set; }

        private readonly List<NetworkBehaviour> _behaviours = new();
        private readonly Dictionary<int, List<(int Hash, Variant Value)>> _changedByGroup = new();

        public int RegisteredCount => _behaviours.Count;
        public IReadOnlyList<NetworkBehaviour> AllBehaviours => _behaviours;

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
            NetworkBehaviour.InSessionQuery = () => MultiplayerSession.InSession;

            NetworkBehaviour.SendCommandToHost = (netId, methodHash, args) =>
            {
                PacketSender.SendToHost(new CommandPacket
                {
                    NetId = netId,
                    MethodHash = methodHash,
                    Args = args,
                });
                return true;
            };

            NetworkBehaviour.SendClientRpcToAll = (netId, methodHash, args) =>
            {
                PacketSender.SendToAllClients(new ClientRpcPacket
                {
                    NetId = netId,
                    MethodHash = methodHash,
                    Args = args,
                    TargetPlayerId = ulong.MaxValue,
                });
                return true;
            };

            NetworkBehaviour.SendClientRpcToGroup = (group, netId, methodHash, args) =>
            {
                PacketSender.SendToGroup(group, new ClientRpcPacket
                {
                    NetId = netId,
                    MethodHash = methodHash,
                    Args = args,
                    TargetPlayerId = ulong.MaxValue,
                });
                return true;
            };

            NetworkBehaviour.LocalUserIdQuery = () => MultiplayerSession.LocalUserID;

            NetworkBehaviour.SendTargetRpcToPlayer = (targetPlayer, netId, methodHash, args) =>
            {
                PacketSender.SendToPlayer(targetPlayer, new ClientRpcPacket
                {
                    NetId = netId,
                    MethodHash = methodHash,
                    Args = args,
                    TargetPlayerId = targetPlayer,
                });
                return true;
            };

            App.OnPostLoadScene += OnPostLoadScene;
        }

        private void OnPostLoadScene()
        {
            if (!Utils.IsInGame()) return;
            Game.Instance.OnSpawnComplete += InitWorldTracking;
        }

        private void InitWorldTracking()
        {
            Game.Instance.Subscribe(1983128072, OnActiveWorldChanged);
            if (ClusterManager.Instance != null)
                InterestGroupManager.SubscribeToGroup(ClusterManager.Instance.activeWorldId);
            Game.Instance.OnSpawnComplete -= InitWorldTracking;
        }

        private void OnActiveWorldChanged(object data)
        {
            var tuple = (Tuple<int, int>)data;
            InterestGroupManager.UnsubscribeFromGroup(tuple.second);
            InterestGroupManager.SubscribeToGroup(tuple.first);
        }

        private void OnDestroy()
        {
            NetworkBehaviour.OnSpawned -= Register;
            NetworkBehaviour.OnBehaviourCleanUp -= Unregister;

            App.OnPostLoadScene -= OnPostLoadScene;

            if (Game.Instance != null)
                Game.Instance.Unsubscribe(1983128072, OnActiveWorldChanged);

            if (Instance == this)
                Instance = null;
        }

        private void Register(NetworkBehaviour behaviour)
        {
            if (!_behaviours.Contains(behaviour))
                _behaviours.Add(behaviour);

            if (behaviour.InterestGroup == -1)
            {
                int worldId = behaviour.GetMyWorldId();
                if (worldId >= 0)
                    behaviour.InterestGroup = worldId;
            }
        }

        private void Unregister(NetworkBehaviour behaviour)
        {
            _behaviours.Remove(behaviour);
        }

        private void Update()
        {
            if (!MultiplayerSession.IsHost) return;
            if (_behaviours.Count == 0) return;

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

                _changedByGroup.Clear();
                var fields = behaviour.SyncVarFields;

                for (int j = 0; j < fields.Count; j++)
                {
                    var field = fields[j];
                    var currentValue = field.Info.GetValue(behaviour);
                    var currentVariant = ObjectToVariant(currentValue);
                    var lastVariant = ObjectToVariant(field.LastSentValue);

                    if (ValuesDiffer(currentVariant, lastVariant, field.Epsilon))
                    {
                        int group = field.InterestGroup;
                        if (group == -1) group = behaviour.InterestGroup;
                        if (!_changedByGroup.TryGetValue(group, out var list))
                        {
                            list = new List<(int Hash, Variant Value)>();
                            _changedByGroup[group] = list;
                        }
                        list.Add((field.Hash, currentVariant));
                    }
                }

                if (_changedByGroup.Count == 0) continue;

                var identity = behaviour.GetComponent<NetworkIdentity>();
                if (identity == null || identity.NetId == 0)
                    continue;

                int netId = identity.NetId;
                long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                foreach (var kvp in _changedByGroup)
                {
                    int groupId = kvp.Key;
                    var updates = kvp.Value;

                    if (updates.Count == 1)
                    {
                        var update = updates[0];
                        PacketSender.SendToGroup(groupId, new SyncVarPacket
                        {
                            NetId = netId,
                            FieldHash = update.Hash,
                            Value = update.Value,
                            Timestamp = timestamp,
                        }, PacketSendMode.Unreliable);
                    }
                    else
                    {
                        var batch = new SyncVarBatchPacket(netId, updates)
                        {
                            Timestamp = timestamp,
                        };
                        PacketSender.SendToGroup(groupId, batch, PacketSendMode.Unreliable);
                    }
                }

                behaviour.SyncLastSentValues();

                int currentWorld = behaviour.GetMyWorldId();
                if (currentWorld >= 0 && currentWorld != behaviour.InterestGroup)
                    behaviour.InterestGroup = currentWorld;
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
    }
}
