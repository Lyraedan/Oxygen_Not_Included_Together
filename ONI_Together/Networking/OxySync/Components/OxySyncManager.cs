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
        private readonly List<(int Hash, Variant Value)> _changedScratch = new();

        public int RegisteredCount => _behaviours.Count;
        public IReadOnlyList<NetworkBehaviour> AllBehaviours => _behaviours;
        
        private readonly Dictionary<NetworkBehaviour, NetworkIdentity> _identityCache = new();

        private void Awake()
        {
            Instance = this;

            NetworkBehaviour.OnSpawned += Register;
            NetworkBehaviour.OnBehaviourCleanUp += Unregister;

            NetworkBehaviour.NetIdQuery = (behaviour) =>
                _identityCache.TryGetValue(behaviour, out var identity) && identity != null
                    ? identity.NetId
                    : 0;

            NetworkBehaviour.NetIdSetter = (behaviour, newNetId) =>
            {
                var identity = ResolveIdentity(behaviour);
                if (identity != null)
                    identity.OverrideNetId(newNetId);
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
            // Don't cache null — identity may be added later (e.g. GameClock)
            var identity = behaviour.GetComponent<NetworkIdentity>();
            if (identity != null)
                _identityCache[behaviour] = identity;
        }

        private void Unregister(NetworkBehaviour behaviour)
        {
            _behaviours.Remove(behaviour);
            _identityCache.Remove(behaviour);
        }

        private NetworkIdentity? ResolveIdentity(NetworkBehaviour behaviour)
        {
            if (_identityCache.TryGetValue(behaviour, out var identity) && !identity.IsNullOrDestroyed())
                return identity;

            identity = behaviour.GetComponent<NetworkIdentity>();
            if (identity != null)
                _identityCache[behaviour] = identity;

            return identity;
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

                _changedScratch.Clear();
                var fields = behaviour.SyncVarFields;

                for (int j = 0; j < fields.Count; j++)
                {
                    var field = fields[j];
                    var currentValue = field.Info.GetValue(behaviour);
                    var currentVariant = ObjectToVariant(currentValue);
                    var lastVariant = ObjectToVariant(field.LastSentValue);

                    if (ValuesDiffer(currentVariant, lastVariant, field.Epsilon))
                    {
                        _changedScratch.Add((field.Hash, currentVariant));
                    }
                }

                if (_changedScratch.Count == 0) continue;

                var identity = ResolveIdentity(behaviour);
                if (identity == null || identity.NetId == 0)
                    continue;

                int netId = identity.NetId;

                if (_changedScratch.Count == 1)
                {
                    var update = _changedScratch[0];
                    PacketSender.SendToAllClients(new SyncVarPacket
                    {
                        NetId = netId,
                        FieldHash = update.Hash,
                        Value = update.Value,
                    }, PacketSendMode.Unreliable);
                }
                else
                {
                    var batch = new SyncVarBatchPacket(netId, _changedScratch);
                    PacketSender.SendToAllClients(batch, PacketSendMode.Unreliable);
                }

                behaviour.SyncLastSentValues();
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
