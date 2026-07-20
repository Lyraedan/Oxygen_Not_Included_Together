using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.World;
using System.Collections.Generic;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Components
{
    public class LogicStateSyncer : KMonoBehaviour
    {
        public static LogicStateSyncer Instance { get; private set; }

        private const float SEND_INTERVAL = 1f;
        private const float INIT_DELAY = 5f;
        private const float CLIENT_STALE_THRESHOLD = 2f;
        private const float CLIENT_REQUEST_COOLDOWN = 0.5f;

        private float _timer;
        private bool _initialized;
        private float _initTime;

        // NetId -> tracked building
        private readonly Dictionary<int, BuildingEntry> _tracked = new();

        // Client: last packet time per building for stale detection
        private readonly Dictionary<int, float> _lastPacketTime = new();
        private float _clientRequestTimer;

        // Viewport scratch buffer
        private readonly HashSet<ulong> _viewportScratch = new();

        private class BuildingEntry
        {
            public GameObject go;
            public Variant lastValue;
            public bool lastActive;
            public Dictionary<string, Variant> lastOptional;
        }

        public override void OnSpawn()
        {
            base.OnSpawn();
            Instance = this;
        }

        public override void OnCleanUp()
        {
            base.OnCleanUp();
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            if (!MultiplayerSession.SessionHasPlayers) return;

            if (MultiplayerSession.IsHost)
                HostUpdate();
            else
                ClientUpdate();
        }

        private void HostUpdate()
        {
            if (!_initialized)
            {
                _initTime = Time.unscaledTime;
                _initialized = true;
                return;
            }
            if (Time.unscaledTime - _initTime < INIT_DELAY) return;

            _timer += Time.unscaledDeltaTime;
            if (_timer < SEND_INTERVAL) return;
            _timer = 0f;

            if (_tracked.Count == 0) return;

            // Collect all cells for viewport culling in one batch
            var toRemove = new List<int>();

            foreach (var kvp in _tracked)
            {
                int netId = kvp.Key;
                var entry = kvp.Value;

                if (entry.go.IsNullOrDestroyed())
                {
                    toRemove.Add(netId);
                    continue;
                }

                if (!SampleBuilding(entry.go, out var value, out var active, out var optional))
                    continue;

                bool changed = LogicStatePacket.VariantValueChanged(value, entry.lastValue)
                    || active != entry.lastActive
                    || LogicStatePacket.OptionalValuesChanged(optional, entry.lastOptional);

                if (!changed)
                    continue;

                entry.lastValue = value;
                entry.lastActive = active;
                entry.lastOptional = optional;

                int cell = Grid.PosToCell(entry.go);

                var packet = new LogicStatePacket
                {
                    NetId = netId,
                    Cell = cell,
                    Value = value,
                    IsActive = active,
                    OptionalValues = optional,
                };

                if (WorldStateSyncer.Instance != null)
                {
                    WorldStateSyncer.Instance.GetClientsViewingCell(cell, _viewportScratch, 2);
                    foreach (var playerId in _viewportScratch)
                        PacketSender.SendToPlayer(playerId, packet, PacketSendMode.Unreliable);
                }
                else
                {
                    PacketSender.SendToAllClients(packet, PacketSendMode.Unreliable);
                }
            }

            foreach (int netId in toRemove)
                _tracked.Remove(netId);
        }

        private void ClientUpdate()
        {
            if (_tracked.Count == 0) return;

            if (!WorldStateSyncer.TryGetLocalViewport(out var viewport))
                return;

            _clientRequestTimer += Time.unscaledDeltaTime;
            if (_clientRequestTimer < CLIENT_REQUEST_COOLDOWN) return;
            _clientRequestTimer = 0f;

            foreach (var kvp in _tracked)
            {
                int netId = kvp.Key;
                var entry = kvp.Value;

                if (entry.go.IsNullOrDestroyed())
                    continue;

                int cell = Grid.PosToCell(entry.go);
                if (!WorldStateSyncer.IsCellInRect(cell, viewport))
                    continue;

                if (!_lastPacketTime.TryGetValue(netId, out var lastTime) || Time.unscaledTime - lastTime > CLIENT_STALE_THRESHOLD)
                {
                    PacketSender.SendToHost(new StructureStateRequestPacket
                    {
                        NetId = netId,
                        RequesterId = MultiplayerSession.LocalUserID,
                    }, PacketSendMode.ReliableImmediate);
                }
            }
        }

        public void HandlePacket(LogicStatePacket packet)
        {
            if (!Grid.IsValidCell(packet.Cell)) return;

            _lastPacketTime[packet.NetId] = Time.unscaledTime;

            if (!_tracked.TryGetValue(packet.NetId, out var entry))
                return;

            if (entry.go.IsNullOrDestroyed())
                return;

            ApplyBuildingState(entry.go, packet);
        }

        public void SendStateToClient(ulong playerId, int netId)
        {
            if (!_tracked.TryGetValue(netId, out var entry))
                return;

            if (entry.go.IsNullOrDestroyed())
                return;

            if (!SampleBuilding(entry.go, out var value, out var active, out var optional))
                return;

            int cell = Grid.PosToCell(entry.go);

            PacketSender.SendToPlayer(playerId, new LogicStatePacket
            {
                NetId = netId,
                Cell = cell,
                Value = value,
                IsActive = active,
                OptionalValues = optional,
            }, PacketSendMode.ReliableImmediate);
        }

        public void Register(GameObject go)
        {
            if (go.IsNullOrDestroyed()) return;
            //if (!MultiplayerSession.SessionHasPlayers) return;

            var identity = go.GetNetIdentity();
            if (identity == null || identity.NetId == 0) return;

            int netId = identity.NetId;
            if (_tracked.ContainsKey(netId)) return;

            _tracked[netId] = new BuildingEntry
            {
                go = go,
                lastValue = default,
                lastActive = false,
                lastOptional = null,
            };
        }

        public void Unregister(GameObject go)
        {
            if (go.IsNullOrDestroyed()) return;

            var identity = go.GetComponent<NetworkIdentity>();
            if (identity == null) return;

            _tracked.Remove(identity.NetId);
            _lastPacketTime.Remove(identity.NetId);
        }

        private bool SampleBuilding(GameObject go, out Variant value, out bool active, out Dictionary<string, Variant> optional)
        {
            value = default;
            active = false;
            optional = new Dictionary<string, Variant>();

            // Switch subclasses: sensors, LogicSwitch, LogicCounter, LogicHammer, TimerSensor, TimeOfDaySensor
            var sw = go.GetComponent<Switch>();
            if (sw != null)
            {
                value = (Variant)(sw.IsSwitchedOn ? 1 : 0);
                var op = go.GetComponent<Operational>();
                active = op != null && op.IsActive;
            }

            // LogicGate (base gates: AND, OR, NOT, XOR, multiplexer, demux)
            var gate = go.GetComponent<LogicGate>();
            if (gate != null)
            {
                // output values are already serialized bits of gate logic
                optional["out1"] = gate.outputValueOne;
                optional["out2"] = gate.outputValueTwo;
                optional["out3"] = gate.outputValueThree;
                optional["out4"] = gate.outputValueFour;

                // LogicGateFilter specific
                var filter = go.GetComponent<LogicGateFilter>();
                if (filter != null)
                {
                    optional["ticksRemaining"] = filter.delayTicksRemaining;
                    optional["wasNegative"] = filter.input_was_previously_negative ? 1 : 0;
                }

                // LogicGateBuffer specific
                var buffer = go.GetComponent<LogicGateBuffer>();
                if (buffer != null)
                {
                    optional["ticksRemaining"] = buffer.delayTicksRemaining;
                    optional["wasPositive"] = buffer.input_was_previously_positive ? 1 : 0;
                }
            }

            // LogicCounter extends Switch, but has extra state beyond switchedOn
            var counter = go.GetComponent<LogicCounter>();
            if (counter != null)
            {
                optional["currentCount"] = counter.currentCount;
                optional["wasResetting"] = counter.wasResetting ? 1 : 0;
                optional["wasIncrementing"] = counter.wasIncrementing ? 1 : 0;
                optional["receivedFirstSignal"] = counter.receivedFirstSignal ? 1 : 0;
            }

            // LogicMemory
            var memory = go.GetComponent<LogicMemory>();
            if (memory != null)
            {
                value = (Variant)(memory.value != 0 ? 1 : 0);
            }

            // LogicTimerSensor extends Switch, extra time tracking
            var timer = go.GetComponent<LogicTimerSensor>();
            if (timer != null)
            {
                optional["timeElapsed"] = timer.timeElapsedInCurrentState;
            }

            // LogicRibbonReader
            var ribbonReader = go.GetComponent<LogicRibbonReader>();
            if (ribbonReader != null)
            {
                value = (Variant)ribbonReader.selectedBit;
                optional["currentValue"] = ribbonReader.currentValue;
                optional["type"] = "reader";
            }

            // LogicRibbonWriter
            var ribbonWriter = go.GetComponent<LogicRibbonWriter>();
            if (ribbonWriter != null)
            {
                value = (Variant)ribbonWriter.selectedBit;
                optional["currentValue"] = ribbonWriter.currentValue;
                optional["type"] = "writer";
            }

            // Automatable
            var automatable = go.GetComponent<Automatable>();
            if (automatable != null)
            {
                value = (Variant)(automatable.GetAutomationOnly() ? 1 : 0);
            }

            // If we found nothing, skip this building
            if (sw == null && gate == null && memory == null && ribbonReader == null && ribbonWriter == null && automatable == null)
                return false;

            return true;
        }

        private void ApplyBuildingState(GameObject go, LogicStatePacket packet)
        {
            // Switch subclasses
            var sw = go.GetComponent<Switch>();
            if (sw != null)
            {
                bool targetOn = packet.Value.Int != 0 || (packet.Value.Type == Variant.TypeCode.Float && packet.Value.Float > 0.5f);
                if (sw.IsSwitchedOn != targetOn)
                    sw.SetState(targetOn);
            }

            // LogicGate output values
            var gate = go.GetComponent<LogicGate>();
            if (gate != null && packet.OptionalValues.TryGetValue("out1", out var out1))
            {
                gate.outputValueOne = out1.Int;
                if (packet.OptionalValues.TryGetValue("out2", out var out2))
                    gate.outputValueTwo = out2.Int;
                if (packet.OptionalValues.TryGetValue("out3", out var out3))
                    gate.outputValueThree = out3.Int;
                if (packet.OptionalValues.TryGetValue("out4", out var out4))
                    gate.outputValueFour = out4.Int;
            }

            // LogicGateFilter ticks
            var filter = go.GetComponent<LogicGateFilter>();
            if (filter != null)
            {
                if (packet.OptionalValues.TryGetValue("ticksRemaining", out var ticks))
                    filter.delayTicksRemaining = ticks.Int;
                if (packet.OptionalValues.TryGetValue("wasNegative", out var wasNeg))
                    filter.input_was_previously_negative = wasNeg.Int != 0;
            }

            // LogicGateBuffer ticks
            var buffer = go.GetComponent<LogicGateBuffer>();
            if (buffer != null)
            {
                if (packet.OptionalValues.TryGetValue("ticksRemaining", out var ticks))
                    buffer.delayTicksRemaining = ticks.Int;
                if (packet.OptionalValues.TryGetValue("wasPositive", out var wasPos))
                    buffer.input_was_previously_positive = wasPos.Int != 0;
            }

            // LogicCounter
            var counter = go.GetComponent<LogicCounter>();
            if (counter != null)
            {
                if (packet.OptionalValues.TryGetValue("currentCount", out var count))
                    counter.currentCount = count.Int;
                if (packet.OptionalValues.TryGetValue("wasResetting", out var wasRst))
                    counter.wasResetting = wasRst.Int != 0;
                if (packet.OptionalValues.TryGetValue("wasIncrementing", out var wasInc))
                    counter.wasIncrementing = wasInc.Int != 0;
                if (packet.OptionalValues.TryGetValue("receivedFirstSignal", out var rfs))
                    counter.receivedFirstSignal = rfs.Int != 0;
            }

            // LogicMemory
            var memory = go.GetComponent<LogicMemory>();
            if (memory != null)
            {
                bool targetOn = packet.Value.Int != 0 || (packet.Value.Type == Variant.TypeCode.Float && packet.Value.Float > 0.5f);
                if (memory.value != (targetOn ? 1 : 0))
                    memory.value = targetOn ? 1 : 0;
            }

            // LogicTimerSensor
            var timer = go.GetComponent<LogicTimerSensor>();
            if (timer != null)
            {
                if (packet.OptionalValues.TryGetValue("timeElapsed", out var elapsed))
                    timer.timeElapsedInCurrentState = elapsed.Float;
            }

            // LogicRibbonReader
            var ribbonReader = go.GetComponent<LogicRibbonReader>();
            if (ribbonReader != null)
            {
                ribbonReader.selectedBit = packet.Value.Int;
                if (packet.OptionalValues.TryGetValue("currentValue", out var val))
                    ribbonReader.currentValue = val.Int;
            }

            // LogicRibbonWriter
            var ribbonWriter = go.GetComponent<LogicRibbonWriter>();
            if (ribbonWriter != null)
            {
                ribbonWriter.selectedBit = packet.Value.Int;
                if (packet.OptionalValues.TryGetValue("currentValue", out var val))
                    ribbonWriter.currentValue = val.Int;
            }

            // Automatable
            var automatable = go.GetComponent<Automatable>();
            if (automatable != null)
            {
                bool targetOn = packet.Value.Int != 0 || (packet.Value.Type == Variant.TypeCode.Float && packet.Value.Float > 0.5f);
                if (automatable.GetAutomationOnly() != targetOn)
                    automatable.SetAutomationOnly(targetOn);
            }
        }
    }
}
