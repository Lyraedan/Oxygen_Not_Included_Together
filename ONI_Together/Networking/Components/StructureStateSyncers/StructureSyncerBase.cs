using System;
using System.Collections.Generic;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.World;
using Shared.Interfaces.Networking;
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
        protected Dictionary<string, Variant> lastOptionalValues;
        protected bool checkOptionalsValuesForChanges = true;
        protected bool cullByViewport = false;

        private readonly HashSet<ulong> _viewportScratch = new();

        private bool _initialized;
        private float _initializationTime;
        private const float INITIAL_DELAY = 5f;

        private float _lastClientPacketTime;
        private float _clientRequestTimer;
        private const float CLIENT_REQUEST_COOLDOWN = 0.5f;
        private const float CLIENT_STALE_THRESHOLD = 2f;

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
            else
            {
                ClientUpdate();
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

            bool changed = StructureStatePacket.VariantValueChanged(currentValue, lastSentValue) ||
                currentActive != lastSentActive ||
                (checkOptionalsValuesForChanges && StructureStatePacket.OptionalValuesChanged(optionalValues, lastOptionalValues)) ||
                ShouldForceSync();

            if (changed)
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

                if (cullByViewport && WorldStateSyncer.Instance != null)
                {
                    WorldStateSyncer.Instance.GetClientsViewingCell(cell, _viewportScratch, 2);
                    foreach (var playerId in _viewportScratch)
                    {
                        PacketSender.SendToPlayer(playerId, packet, PacketSendMode.Unreliable);
                    }
                }
                else
                {
                    PacketSender.SendToAllClients(packet, PacketSendMode.Unreliable);
                }
            }
        }

        private void ClientUpdate()
        {
            if (!WorldStateSyncer.TryGetLocalViewport(out var viewport))
                return;

            if (!WorldStateSyncer.IsCellInRect(cell, viewport))
                return;

            if (_lastClientPacketTime == 0 || Time.unscaledTime - _lastClientPacketTime > CLIENT_STALE_THRESHOLD)
            {
                _clientRequestTimer += Time.unscaledDeltaTime;
                if (_clientRequestTimer >= CLIENT_REQUEST_COOLDOWN)
                {
                    _clientRequestTimer = 0f;

                    var identity = gameObject.GetNetIdentity();
                    if (identity.NetId == 0) return;

                    PacketSender.SendToHost(new StructureStateRequestPacket
                    {
                        NetId = identity.NetId,
                        RequesterId = MultiplayerSession.LocalUserID,
                    }, PacketSendMode.ReliableImmediate);
                }
            }
        }

        public void SendStateToClient(ulong playerId)
        {
            SampleState(out var value, out var active, out var optionalValues);
            if (operational != null)
                active = operational.IsActive;

            var identity = gameObject.GetNetIdentity();
            if (identity.NetId == 0) return;

            var packet = new StructureStatePacket
            {
                NetId = identity.NetId,
                Cell = cell,
                Value = value,
                IsActive = active,
                OptionalValues = optionalValues,
            };

            PacketSender.SendToPlayer(playerId, packet, PacketSendMode.ReliableImmediate);
        }

        protected abstract void SampleState(out Variant value, out bool active, out Dictionary<string, Variant> optionalValues);
        protected abstract void ApplyState(StructureStatePacket packet);

        protected abstract bool ShouldForceSync();

        public void HandlePacket(StructureStatePacket packet)
        {
            if (!Grid.IsValidCell(packet.Cell)) return;

            if (!MultiplayerSession.IsHost)
                _lastClientPacketTime = Time.unscaledTime;

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
