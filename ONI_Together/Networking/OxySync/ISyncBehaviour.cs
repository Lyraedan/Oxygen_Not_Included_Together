using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ONI_Together.Networking.OxySync
{
    /// <summary>
    /// A neutral read/write descriptor for a single SyncVar field, decoupled from
    /// <see cref="Shared.OxySync.NetworkBehaviour.SyncVarField"/> so the sync loop can
    /// handle both native behaviours and behaviours that live in the ONI Together API assembly.
    /// </summary>
    public struct SyncVarDescriptor
    {
        public string Name;
        public int Hash;
        public Type FieldType;
        public object LastSentValue;
        public float Epsilon;
        public int InterestGroup;
        public int SendMode;

        private FieldInfo _field;
        private object _instance;

        public static SyncVarDescriptor FromField(object instance, FieldInfo field, int hash,
            object lastSentValue, float epsilon, int interestGroup, int sendMode)
        {
            return new SyncVarDescriptor
            {
                Name = field.Name,
                Hash = hash,
                FieldType = field.FieldType,
                LastSentValue = lastSentValue,
                Epsilon = epsilon,
                InterestGroup = interestGroup,
                SendMode = sendMode,
                _field = field,
                _instance = instance,
            };
        }

        public object GetValue() => _field?.GetValue(_instance);

        public void SetValue(object value)
        {
            if (_field != null)
                _field.SetValue(_instance, value);
            LastSentValue = value;
        }
    }

    /// <summary>
    /// A neutral descriptor for a Command / ClientRpc / TargetRpc method, decoupled from
    /// <c>Shared.OxySync.NetworkBehaviour.CachedMethod</c> so the debug tool can present and
    /// invoke both native and API-bridged behaviours.
    /// </summary>
    public struct RpcDescriptor
    {
        public string Name;
        public int Hash;
        public Type[] ArgTypes;
        public int InterestGroup;
    }

    /// <summary>
    /// Common surface implemented by native OxySync behaviours and by the API-bridge
    /// adapter, so <c>OxySyncManager</c> can drive a single list of behaviours.
    /// </summary>
    public interface ISyncBehaviour
    {
        int NetId { get; set; }
        int BehaviourId { get; set; }
        int InterestGroup { get; set; }

        float SyncInterval { get; }
        float LastSyncTime { get; set; }
        float LastActiveSyncTime { get; set; }

        GameObject GameObject { get; }
        bool IsDestroyed { get; }
        int GetMyWorldId();

        /// <summary>
        /// The actual behaviour Type (native or API), used for attribute-based lookups like
        /// <c>[FixedInterestGroup]</c> and world-group resolution.
        /// </summary>
        Type UnderlyingType { get; }

        /// <summary>
        /// The assembly the behaviour's OxySync copy lives in (the main mod for native
        /// behaviours, otherwise the consuming mod's API copy). Used for display only.
        /// </summary>
        System.Reflection.Assembly SourceAssembly { get; }

        int SyncVarCount { get; }
        SyncVarDescriptor GetSyncVar(int index);

        /// <summary>
        /// Refreshes cached SyncVar metadata (values, last-sent, etc). Native behaviours are
        /// already current and treat this as a no-op; the API bridge rebuilds its descriptors.
        /// Called at most once per behaviour per sync tick.
        /// </summary>
        void RefreshSyncVars();

        ulong GetAndClearDirtyBits();
        void MarkAllDirty();

        /// <summary>Flags a single SyncVar (by field hash) as dirty so it ships on the next tick.</summary>
        void MarkSyncVarDirty(int fieldHash);

        void SyncLastSentValues();

        /// <summary>Current (non-destructive) SyncVar dirty mask.</summary>
        ulong SyncVarDirtyBits { get; }

        void ApplySyncVar(int fieldHash, object value, long timestamp);

        /// <summary>Command methods declared on the behaviour (client -> host).</summary>
        IReadOnlyList<RpcDescriptor> Commands { get; }

        /// <summary>ClientRpc methods declared on the behaviour (host -> clients).</summary>
        IReadOnlyList<RpcDescriptor> ClientRpcs { get; }

        /// <summary>TargetRpc methods declared on the behaviour (host -> one client).</summary>
        IReadOnlyList<RpcDescriptor> TargetRpcs { get; }

        void InvokeCommand(int methodHash, byte[] args);
        void InvokeClientRpc(int methodHash, byte[] args);
        void InvokeTargetRpc(int methodHash, byte[] args);
    }
}
