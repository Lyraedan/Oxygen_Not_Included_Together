using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Klei.AI;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components
{
    public class VitalStatsSyncComponent : NetworkBehaviour
    {
        [MyCmpReq]
        private NetworkIdentity _identity;
        [MyCmpReq]
        private PrimaryElement _element;
        private Amounts _amounts;

        private float _syncTimer;

        [SyncVar(Hook = nameof(OnSyncedVitalsChanged))]
        private byte[] _syncedVitals = Array.Empty<byte>();

        public override void OnSpawn()
        {
            base.OnSpawn();
            _amounts = gameObject.GetAmounts();
        }

        private void Update()
        {
            if (!MultiplayerSession.IsHostInSession || !MultiplayerSession.SessionHasPlayers)
                return;

            _syncTimer += Time.unscaledDeltaTime;
            if (_syncTimer < 1f) return;
            _syncTimer = 0f;

            using var _ = Profiler.Scope();

            try
            {
                var sw = Stopwatch.StartNew();
                var data = SerializeVitals();
                _syncedVitals = data;
                sw.Stop();
                SyncStats.RecordSync(SyncStats.VitalStats, 1, data.Length, (float)sw.Elapsed.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[VitalStatsSyncComponent] Exception: {ex}");
            }
        }

        private void OnSyncedVitalsChanged(byte[] oldData, byte[] newData)
        {
            using var _ = Profiler.Scope();

            try
            {
                Apply(newData);
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[VitalStatsSyncComponent.OnSyncedVitalsChanged] Exception: {ex}");
            }
        }

        private byte[] SerializeVitals()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(_element.DiseaseIdx);
            writer.Write(_element.DiseaseCount);

            var amounts = _amounts.ModifierList;
            writer.Write(amounts.Count);
            foreach (var amountInstance in amounts)
            {
                writer.Write(amountInstance.amount.Id);
                writer.Write(amountInstance.value);
            }

            return ms.ToArray();
        }

        private void Apply(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            byte targetDiseaseIdx = reader.ReadByte();
            int targetDiseaseCount = reader.ReadInt32();

            int amountsCount = reader.ReadInt32();
            var vitalAmounts = new Dictionary<string, float>(amountsCount);
            for (int i = 0; i < amountsCount; i++)
            {
                string key = reader.ReadString();
                float value = reader.ReadSingle();
                vitalAmounts[key] = value;
            }

            var identity = _identity;
            if (identity.IsNullOrDestroyed()) return;

            var amounts = identity.GetAmounts();
            if (amounts == null) return;

            foreach (var kvp in vitalAmounts)
            {
                amounts.SetValue(kvp.Key, kvp.Value);
            }

            if (identity.TryGetComponent<PrimaryElement>(out var element))
            {
                int currentDiseaseCount = element.DiseaseCount;
                int currentDiseaseIdx = element.DiseaseIdx;
                if (currentDiseaseIdx != targetDiseaseIdx)
                {
                    element.AddDisease(targetDiseaseIdx, targetDiseaseCount, "MP-Mod.SyncedDisease");
                }
                else if (!Mathf.Approximately(currentDiseaseCount, targetDiseaseCount))
                    element.ModifyDiseaseCount(targetDiseaseCount - currentDiseaseCount, "MP-Mod.SyncedDisease");
            }
        }
    }
}
