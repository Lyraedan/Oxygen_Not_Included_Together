using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Misc.World;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Components.StructureStateSyncers;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.States;
using UnityEngine;

namespace ONI_Together.Networking.Refresh
{
    internal sealed class ColonyRefreshCoordinator : KMonoBehaviour
    {
        public static ColonyRefreshCoordinator Instance { get; private set; }
        private readonly RefreshGate gate = new();
        private readonly RefreshRecordAssembler assembler = new();
        private IEnumerator<(RefreshRecordKind kind, IPacket packet)> records;
        private byte[] outgoing;
        private RefreshRecordKind outgoingKind;
        private int outgoingOffset, hostSequence, hostDone, hostTotal, hostSkipped;
        private int worldWidth, worldHeight;

        private Guid clientRequest;
        private int expectedSequence, applyIndex;
        private IPacket pendingApply;
        private bool applied = true, lastAckApplied = true;
        private double lastProgress, retryUntil;
        private double lastHostStatus;
        public RefreshState ClientState { get; private set; }
        public int Done { get; private set; }
        public int Total { get; private set; }
        public int Skipped { get; private set; }
        private bool ClientActive => clientRequest != Guid.Empty;
        private static double Now => Time.unscaledTime;
        public int RetrySeconds => Math.Max(0, (int)Math.Ceiling(retryUntil - Now));
        public bool CanRequest => !ClientActive && RetrySeconds == 0 && ClientReady;
        private static bool WorldReady => Game.Instance != null && Grid.WidthInCells > 0 && Grid.HeightInCells > 0;
        private static bool ClientReady => WorldReady && MultiplayerSession.IsClient && GameClient.State == Networking.States.ClientState.InGame
            && !GameClient.IsHardSyncInProgress && MultiplayerSession.ConnectedPlayers.TryGetValue(MultiplayerSession.HostUserID, out var host)
            && host.Connection != null && host.ProtocolVerified;

        public override void OnSpawn()
        {
            base.OnSpawn();
            Instance = this;
            StateRevisions.Terrain.Clear();
        }

        public override void OnCleanUp()
        {
            CancelAll();
            gate.Reset();
            StateRevisions.Terrain.Clear();
            WorldUpdateBatcher.Clear();
            ChunkedPacket.ClearPending();
            if (Instance == this) Instance = null;
            base.OnCleanUp();
        }

        public void Request()
        {
            if (!CanRequest) return;
            clientRequest = Guid.NewGuid();
            expectedSequence = applyIndex = Done = Total = Skipped = 0;
            assembler.Clear();
            pendingApply = null;
            worldWidth = Grid.WidthInCells; worldHeight = Grid.HeightInCells;
            lastProgress = Now;
            ClientState = RefreshState.Waiting;
            if (!Send(MultiplayerSession.HostUserID, new ColonyRefreshRequestPacket { RequestId = clientRequest }))
                StopClient(RefreshState.Failed, false);
        }

        private bool FromHost(ColonyRefreshPacket packet) => MultiplayerSession.IsClient
            && RefreshRouting.IsResponseFor(clientRequest, MultiplayerSession.HostUserID, packet.RequestId, packet.SenderId);

        private static bool Send(ulong player, IPacket packet)
        {
            try { return PacketSender.SendToPlayer(player, packet); }
            catch (Exception ex) { DebugConsole.LogWarning($"[ColonyRefresh] Send to {player} failed: {ex}"); return false; }
        }

        public void ReceiveRequest(ColonyRefreshRequestPacket packet)
        {
            if (!MultiplayerSession.IsHostInSession || !packet.SenderId.HasValue) return;
            ulong player = packet.SenderId.Value;
            if (player == MultiplayerSession.HostUserID || !MultiplayerSession.ConnectedPlayers.TryGetValue(player, out var peer)
                || peer.Connection == null || !peer.ProtocolVerified) return;
            if (packet.Cancel)
            {
                if (gate.Active && gate.PlayerId == player && gate.RequestId == packet.RequestId) FinishHost(RefreshState.Cancelled);
                return;
            }
            if (gate.Active && gate.PlayerId == player && gate.RequestId == packet.RequestId)
            {
                SendStatus(player, packet.RequestId, RefreshState.Accepted);
                return; // An identical request must not restart or reject an accepted transfer.
            }
            if (!WorldReady || peer.readyState != ClientReadyState.Ready || GameServerHardSync.IsHardSyncInProgress)
            {
                SendStatus(player, packet.RequestId, RefreshState.Unavailable);
                return;
            }
            var result = gate.Begin(player, packet.RequestId, Now, out int retry);
            if (result != RefreshState.Accepted)
            {
                SendStatus(player, packet.RequestId, result, retry);
                return;
            }
            worldWidth = Grid.WidthInCells; worldHeight = Grid.HeightInCells;
            int[] identities = NetworkIdentityRegistry.AllIdentities.Where(i => i != null).Select(i => i.NetId).ToArray();
            int[] logic = LogicStateSyncer.Instance?.TrackedIds ?? Array.Empty<int>();
            hostDone = hostSkipped = hostSequence = 0;
            hostTotal = ((worldWidth + 31) / 32) * ((worldHeight + 31) / 32) + identities.Length + logic.Length;
            records = CaptureRecords(identities, logic).GetEnumerator();
            SendStatus(player, packet.RequestId, RefreshState.Accepted);
            DebugConsole.Log($"[ColonyRefresh] Accepted {packet.RequestId} for player {player}, {hostTotal} work units");
        }

        private IEnumerable<(RefreshRecordKind, IPacket)> CaptureRecords(int[] identities, int[] logic)
        {
            foreach (var region in RefreshLayout.Regions(worldWidth, worldHeight))
            {
                var packet = new WorldUpdatePacket();
                var captureBudget = Stopwatch.StartNew();
                for (int y = region.Y; y < region.Y + region.Height; y++)
                    for (int x = region.X; x < region.X + region.Width; x++)
                    {
                        int cell = Grid.XYToCell(x, y);
                        packet.Updates.Add(WorldUpdatePacket.CaptureCell(cell));
                        if (captureBudget.Elapsed.TotalMilliseconds >= 2)
                        {
                            yield return (RefreshRecordKind.Terrain, null);
                            captureBudget.Restart();
                        }
                    }
                yield return (RefreshRecordKind.Terrain, packet);
                hostDone++;
            }
            foreach (int netId in identities)
            {
                if (NetworkIdentityRegistry.TryGet(netId, out var identity))
                    foreach (var syncer in identity.GetComponents<StructureSyncerBase>())
                    {
                        StructureStatePacket packet = null;
                        try { packet = syncer.CaptureState(); }
                        catch (Exception ex) { DebugConsole.LogWarning($"[ColonyRefresh] Capture failed for {netId}: {ex}"); }
                        if (packet == null) hostSkipped++;
                        yield return (RefreshRecordKind.Structure, packet);
                    }
                else hostSkipped++;
                hostDone++;
                yield return (RefreshRecordKind.Structure, null);
            }
            foreach (int netId in logic)
            {
                LogicStatePacket packet = null;
                try { packet = LogicStateSyncer.Instance?.CaptureState(netId); }
                catch (Exception ex) { DebugConsole.LogWarning($"[ColonyRefresh] Logic capture failed for {netId}: {ex}"); }
                if (packet == null) hostSkipped++;
                yield return (RefreshRecordKind.Logic, packet);
                hostDone++;
            }
        }

        public void ReceiveAck(ColonyRefreshAckPacket packet)
        {
            if (!MultiplayerSession.IsHost || !packet.SenderId.HasValue
                || !gate.Acknowledge(packet.SenderId.Value, packet.RequestId, packet.Sequence, Now)) return;
            if (!packet.Applied) hostSkipped++;
            if (outgoingOffset == outgoing?.Length) { outgoing = null; outgoingOffset = 0; }
            SendStatus(gate.PlayerId, gate.RequestId, RefreshState.Accepted);
        }

        public void ReceiveStatus(ColonyRefreshStatusPacket packet)
        {
            if (!FromHost(packet)) return;
            lastProgress = Now;
            Done = packet.Done; Total = packet.Total; Skipped = packet.Skipped;
            if (packet.State == RefreshState.Accepted && ClientState == RefreshState.Waiting) retryUntil = Now + RefreshLimits.Cooldown;
            ClientState = packet.State;
            if (packet.State != RefreshState.Accepted)
            {
                if (packet.State == RefreshState.Cooldown) retryUntil = Now + packet.RetrySeconds;
                StopClient(packet.State, false);
            }
        }

        public void ReceiveBatch(ColonyRefreshBatchPacket packet)
        {
            if (!FromHost(packet)) return;
            if (packet.Sequence == expectedSequence - 1) { Ack(packet.Sequence, lastAckApplied); return; }
            if (packet.Sequence != expectedSequence || pendingApply != null) return;
            try
            {
                byte[] data = assembler.Append(packet.Kind, packet.RecordLength, packet.Offset, packet.Data);
                lastProgress = Now;
                if (data == null) { CompleteBatch(true); return; }
                pendingApply = packet.Kind switch
                {
                    RefreshRecordKind.Terrain => new WorldUpdatePacket(),
                    RefreshRecordKind.Structure => new StructureStatePacket(),
                    RefreshRecordKind.Logic => new LogicStatePacket(),
                    _ => throw new InvalidDataException("Unknown refresh record")
                };
                using var reader = new BinaryReader(new MemoryStream(data));
                pendingApply.Deserialize(reader);
                if (reader.BaseStream.Position != data.Length || pendingApply is WorldUpdatePacket terrain && terrain.Updates.Count > 1024)
                    throw new InvalidDataException("Invalid refresh record length");
                applyIndex = 0;
                applied = true;
            }
            catch (Exception ex)
            {
                DebugConsole.LogWarning($"[ColonyRefresh] Invalid record: {ex}");
                StopClient(RefreshState.Failed, true);
            }
        }

        private void Ack(int sequence, bool success) => Send(MultiplayerSession.HostUserID, new ColonyRefreshAckPacket
        { RequestId = clientRequest, Sequence = sequence, Applied = success });

        private void CompleteBatch(bool success)
        {
            lastAckApplied = success;
            Ack(expectedSequence++, success);
            lastProgress = Now;
            pendingApply = null;
        }

        private void Update()
        {
            if (gate.Active)
            {
                if (!MultiplayerSession.IsHostInSession || !WorldReady || Grid.WidthInCells != worldWidth || Grid.HeightInCells != worldHeight
                    || GameServerHardSync.IsHardSyncInProgress || !MultiplayerSession.ConnectedPlayers.TryGetValue(gate.PlayerId, out var peer)
                    || peer.Connection == null || peer.readyState != ClientReadyState.Ready) FinishHost(RefreshState.Cancelled);
                else if (gate.Expired(Now)) FinishHost(RefreshState.Failed);
                else if (gate.AwaitingSequence < 0)
                {
                    try { PumpHost(); }
                    catch (Exception ex) { DebugConsole.LogError($"[ColonyRefresh] Host refresh failed: {ex}"); FinishHost(RefreshState.Failed); }
                }
            }
            if (ClientActive)
            {
                if (!ClientReady || Grid.WidthInCells != worldWidth || Grid.HeightInCells != worldHeight) StopClient(RefreshState.Cancelled, true);
                else if (Now - lastProgress >= RefreshLimits.Timeout) StopClient(RefreshState.Failed, true);
                else if (pendingApply != null) PumpClient();
            }
        }

        private void PumpHost()
        {
            var watch = Stopwatch.StartNew();
            for (int work = 0; work < 8 && watch.Elapsed.TotalMilliseconds < 2; work++)
            {
                if (outgoing == null)
                {
                    if (!records.MoveNext()) { FinishHost(hostSkipped == 0 ? RefreshState.Completed : RefreshState.Incomplete); return; }
                    var record = records.Current;
                    gate.Progress(Now);
                    if (record.packet == null)
                    {
                        if (Now - lastHostStatus >= 0.25)
                        {
                            SendStatus(gate.PlayerId, gate.RequestId, RefreshState.Accepted);
                            lastHostStatus = Now;
                        }
                        continue;
                    }
                    using var stream = new MemoryStream();
                    using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) record.packet.Serialize(writer);
                    if (stream.Length > RefreshLimits.RecordBytes) { hostSkipped++; continue; }
                    outgoing = stream.ToArray(); outgoingKind = record.kind;
                }
                int count = Math.Min(RefreshLimits.FragmentBytes, outgoing.Length - outgoingOffset);
                var data = new byte[count];
                Buffer.BlockCopy(outgoing, outgoingOffset, data, 0, count);
                var batch = new ColonyRefreshBatchPacket
                {
                    RequestId = gate.RequestId, Sequence = hostSequence++, Kind = outgoingKind,
                    RecordLength = outgoing.Length, Offset = outgoingOffset, Data = data
                };
                gate.Sent(batch.Sequence);
                outgoingOffset += count;
                if (!Send(gate.PlayerId, batch)) FinishHost(RefreshState.Failed);
                return; // One fragment in flight; wait until the requester applied/accepted it.
            }
        }

        private void PumpClient()
        {
            var watch = Stopwatch.StartNew();
            try
            {
                if (pendingApply is WorldUpdatePacket terrain)
                {
                    int cells = 0;
                    while (applyIndex < terrain.Updates.Count && cells++ < 1024 && watch.Elapsed.TotalMilliseconds < 2)
                        applied &= WorldUpdatePacket.TryApplyCell(terrain.Updates[applyIndex++]);
                    if (applyIndex < terrain.Updates.Count) return;
                }
                else if (pendingApply is StructureStatePacket structure)
                {
                    var syncer = NetworkIdentityRegistry.TryGet(structure.NetId, out var identity)
                        ? identity.GetComponents<StructureSyncerBase>().FirstOrDefault(s => s.GetType().FullName == structure.SyncerType) : null;
                    applied = syncer != null && syncer.TryApplyPacket(structure);
                }
                else if (pendingApply is LogicStatePacket logic)
                    applied = LogicStateSyncer.Instance != null && LogicStateSyncer.Instance.TryApplyPacket(logic);
            }
            catch (Exception ex) { applied = false; DebugConsole.LogWarning($"[ColonyRefresh] Record application failed: {ex}"); }
            CompleteBatch(applied);
        }

        private void SendStatus(ulong player, Guid request, RefreshState state, int retry = 0)
        {
            bool current = gate.Active && request == gate.RequestId && player == gate.PlayerId;
            Send(player, new ColonyRefreshStatusPacket
            {
                RequestId = request, State = state, Done = current ? hostDone : 0, Total = current ? hostTotal : 0,
                Skipped = current ? hostSkipped : 0, RetrySeconds = retry
            });
        }

        private void FinishHost(RefreshState state)
        {
            if (gate.Active) SendStatus(gate.PlayerId, gate.RequestId, state);
            records?.Dispose(); records = null; outgoing = null; outgoingOffset = 0;
            gate.Finish();
        }

        private void StopClient(RefreshState state, bool notify)
        {
            if (ClientActive && notify && MultiplayerSession.IsClient)
                Send(MultiplayerSession.HostUserID, new ColonyRefreshRequestPacket { RequestId = clientRequest, Cancel = true });
            clientRequest = Guid.Empty;
            pendingApply = null;
            assembler.Clear();
            ClientState = state;
        }

        public void CancelAll()
        {
            FinishHost(RefreshState.Cancelled);
            gate.Reset();
            if (ClientActive) StopClient(RefreshState.Cancelled, true);
            retryUntil = 0;
        }
    }
}
