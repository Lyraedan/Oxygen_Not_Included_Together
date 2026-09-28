using System;
using Shared.OxySync;
using UnityEngine;

namespace ONI_Together.Networking.OxySync
{
    /// <summary>
    /// Adapts a native (ONI Together) <see cref="NetworkBehaviour"/> to <see cref="ISyncBehaviour"/>.
    /// This is a thin pass-through with no reflection.
    /// </summary>
    public sealed class NativeSyncBehaviour : ISyncBehaviour
    {
        private readonly NetworkBehaviour _behaviour;

        public NativeSyncBehaviour(NetworkBehaviour behaviour)
        {
            _behaviour = behaviour;
        }

        public NetworkBehaviour Native => _behaviour;

        public int NetId
        {
            get => _behaviour.NetId;
            set => _behaviour.NetId = value;
        }

        public int BehaviourId
        {
            get => _behaviour.BehaviourId;
            set => _behaviour.BehaviourId = value;
        }

        public int InterestGroup
        {
            get => _behaviour.InterestGroup;
            set => _behaviour.InterestGroup = value;
        }

        public float SyncInterval => _behaviour.SyncInterval;
        public float LastSyncTime
        {
            get => _behaviour._lastSyncTime;
            set => _behaviour._lastSyncTime = value;
        }
        public float LastActiveSyncTime
        {
            get => _behaviour._lastActiveSyncTime;
            set => _behaviour._lastActiveSyncTime = value;
        }

        public GameObject GameObject => _behaviour.gameObject;
        public bool IsDestroyed => _behaviour.IsNullOrDestroyed();
        public int GetMyWorldId() => _behaviour.GetMyWorldId();
        public Type UnderlyingType => _behaviour.GetType();
        public System.Reflection.Assembly SourceAssembly => _behaviour.GetType().Assembly;

        public int SyncVarCount => _behaviour.SyncVarFields.Count;

        public void RefreshSyncVars()
        {
        }

        public SyncVarDescriptor GetSyncVar(int index)
        {
            var field = _behaviour.SyncVarFields[index];
            return SyncVarDescriptor.FromField(
                _behaviour,
                field.Info,
                field.Hash,
                field.LastSentValue,
                field.Epsilon,
                field.InterestGroup,
                field.SendMode);
        }

        public ulong GetAndClearDirtyBits() => _behaviour.GetAndClearDirtyBits();
        public void MarkAllDirty() => _behaviour.MarkAllDirty();

        public void MarkSyncVarDirty(int fieldHash)
            => _behaviour.SetSyncVarValue(fieldHash, GetRawValue(fieldHash));

        private object GetRawValue(int fieldHash)
        {
            var fields = _behaviour.SyncVarFields;
            for (int i = 0; i < fields.Count; i++)
                if (fields[i].Hash == fieldHash)
                    return fields[i].Info.GetValue(_behaviour);
            return null;
        }

        public void SyncLastSentValues() => _behaviour.SyncLastSentValues();
        public ulong SyncVarDirtyBits => _behaviour.SyncVarDirtyBits;

        public void ApplySyncVar(int fieldHash, object value, long timestamp)
            => _behaviour.ApplySyncVar(fieldHash, value, timestamp);

        public void InvokeCommand(int methodHash, byte[] args) => _behaviour.InvokeCommand(methodHash, args);
        public void InvokeClientRpc(int methodHash, byte[] args) => _behaviour.InvokeClientRpc(methodHash, args);
        public void InvokeTargetRpc(int methodHash, byte[] args) => _behaviour.InvokeTargetRpc(methodHash, args);

        public System.Collections.Generic.IReadOnlyList<RpcDescriptor> Commands
            => Convert(_behaviour.Commands);

        public System.Collections.Generic.IReadOnlyList<RpcDescriptor> ClientRpcs
            => Convert(_behaviour.ClientRpcs);

        public System.Collections.Generic.IReadOnlyList<RpcDescriptor> TargetRpcs
            => Convert(_behaviour.TargetRpcs);

        private static System.Collections.Generic.IReadOnlyList<RpcDescriptor> Convert(
            System.Collections.Generic.IReadOnlyDictionary<int, NetworkBehaviour.CachedMethod> methods)
        {
            var list = new System.Collections.Generic.List<RpcDescriptor>(methods.Count);
            foreach (var kvp in methods)
            {
                var m = kvp.Value;
                list.Add(new RpcDescriptor
                {
                    Name = m.Info?.Name ?? kvp.Key.ToString(),
                    Hash = m.Hash,
                    ArgTypes = m.ArgTypes,
                    InterestGroup = m.InterestGroup,
                });
            }
            return list;
        }
    }
}
