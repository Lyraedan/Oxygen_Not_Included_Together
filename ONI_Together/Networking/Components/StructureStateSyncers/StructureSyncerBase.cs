using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.World;
using UnityEngine;

namespace ONI_Together.Networking.Components.StructureStateSyncers
{
    public abstract class StructureSyncerBase : KMonoBehaviour
    {
        protected float sendInterval = 0.5f;
        protected float timer;
        protected Operational operational;
        protected int cell;
        protected Variant lastSentValue;
        protected bool lastSentActive;
        protected List<Variant> lastOptionalValues;
        protected bool checkOptionalsValuesForChanges = true; // bypass optional values check

        private bool _initialized;
        private float _initializationTime;
        private const float INITIAL_DELAY = 5f;

        protected abstract void Initialize();

        public override void OnSpawn()
        {
            base.OnSpawn();
            cell = Grid.PosToCell(this);
            operational = GetComponent<Operational>();
            Initialize();
        }

        private void Update()
        {
            //if (!MultiplayerSession.InSession) return;
            if (!MultiplayerSession.SessionHasPlayers) return;
            if (MultiplayerSession.IsHost)
            {
                if (!_initialized)
                {
                    _initializationTime = Time.unscaledTime;
                    _initialized = true;
                    return;
                }
                if (Time.unscaledTime - _initializationTime < INITIAL_DELAY) return;
                HostUpdate();
            }
        }

        private void HostUpdate()
        {
            timer += Time.unscaledDeltaTime;
            if (timer < sendInterval) return;
            timer = 0f;

            SampleState(out var currentValue, out var currentActive, out var optionalValues);

            if (operational != null)
                currentActive = operational.IsActive;

            if (StructureStatePacket.VariantValueChanged(currentValue, lastSentValue) ||
                currentActive != lastSentActive ||
                (checkOptionalsValuesForChanges && StructureStatePacket.OptionalValuesChanged(optionalValues, lastOptionalValues)) ||
                ShouldForceSync())
            {
                lastSentValue = currentValue;
                lastSentActive = currentActive;
                lastOptionalValues = optionalValues;

                var identity = gameObject.GetNetIdentity();
                if (identity.NetId == 0)
                {
                    DebugConsole.Log($"No Net ID found on sync structure of type {GetType().Name}.");
                    return;
                }

                var packet = new StructureStatePacket
                {
                    NetId = identity.NetId,
                    Cell = cell,
                    Value = currentValue,
                    IsActive = currentActive,
                    OptionalValues = optionalValues,
                };

                PacketSender.SendToAllClients(packet, PacketSendMode.Unreliable);
            }
        }

        protected abstract void SampleState(out Variant value, out bool active, out List<Variant> optionalValues);
        protected abstract void ApplyState(StructureStatePacket packet);

        protected abstract bool ShouldForceSync();

        public void HandlePacket(StructureStatePacket packet)
        {
            if (!Grid.IsValidCell(packet.Cell)) return;

            ApplyState(packet);
            ApplyOperationalState(packet);
        }

        private void ApplyOperationalState(StructureStatePacket packet)
        {
            var op = GetComponent<Operational>();
            op?.SetActive(packet.IsActive);
        }
    }
}
