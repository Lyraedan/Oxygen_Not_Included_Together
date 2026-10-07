using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ONI_Together.Misc;
using ONI_Together.Networking.Refresh;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.DebugTools.UnitTests
{
    public static class ColonyRefreshTests
    {
        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        private static UnitTestResult Run(System.Action test)
        { try { test(); return UnitTestResult.Pass(); } catch (Exception ex) { return UnitTestResult.Fail(ex.ToString()); } }
        private static void Invalid(System.Action action)
        {
            try { action(); } catch (InvalidDataException) { return; }
            throw new InvalidOperationException("Expected invalid data to be rejected");
        }

        [UnitTest(name: "Colony refresh covers partial grid edges exactly once", category: "Sync")]
        public static UnitTestResult GridCoverage() => Run(() =>
        {
            var cells = new HashSet<int>();
            foreach (var region in RefreshLayout.Regions(65, 34))
                for (int y = region.Y; y < region.Y + region.Height; y++)
                    for (int x = region.X; x < region.X + region.Width; x++)
                        Check(x < 65 && y < 34 && cells.Add(y * 65 + x), "Region escaped or overlapped grid");
            Check(cells.Count == 65 * 34 && cells.Contains(65 * 34 - 1), "Grid edge was omitted");
            Check(!RefreshLayout.Regions(0, 0).Any(), "Empty grid produced work");
        });

        [UnitTest(name: "Colony refresh enforces a global job and requester cooldown", category: "Sync")]
        public static UnitTestResult CooldownAndBusy() => Run(() =>
        {
            var gate = new RefreshGate();
            Check(gate.Begin(1, Guid.NewGuid(), 0, out _) == RefreshState.Accepted, "First request rejected");
            Check(gate.Begin(2, Guid.NewGuid(), 1, out _) == RefreshState.Busy, "Second client acquired active job");
            gate.Finish();
            Check(gate.Begin(1, Guid.NewGuid(), 29, out int retry) == RefreshState.Cooldown && retry == 1, "Cooldown missing");
            Check(gate.Begin(2, Guid.NewGuid(), 29, out _) == RefreshState.Accepted, "Cooldown applied to another client");
            gate.Finish();
            Check(gate.Begin(1, Guid.NewGuid(), 30, out _) == RefreshState.Accepted, "Cooldown never ended");
        });

        [UnitTest(name: "Only the authenticated requester can acknowledge the current refresh batch", category: "Sync")]
        public static UnitTestResult AckRouting() => Run(() =>
        {
            var gate = new RefreshGate(); var request = Guid.NewGuid();
            gate.Begin(5, request, 0, out _); gate.Sent(0);
            Check(!gate.Acknowledge(6, request, 0, 1), "Another client acknowledged the job");
            Check(!gate.Acknowledge(5, Guid.NewGuid(), 0, 1), "Expired request acknowledged");
            Check(!gate.Acknowledge(5, request, 1, 1), "Wrong sequence acknowledged");
            Check(gate.AwaitingSequence == 0 && gate.Acknowledge(5, request, 0, 2), "Correct acknowledgement rejected");
            Check(!gate.Acknowledge(5, request, 0, 3), "Duplicate acknowledgement advanced job");
        });

        [UnitTest(name: "Refresh timeout measures progress and cancellation releases the job", category: "Sync")]
        public static UnitTestResult TimeoutAndCancellation() => Run(() =>
        {
            var gate = new RefreshGate(); var request = Guid.NewGuid();
            gate.Begin(1, request, 0, out _); gate.Progress(20); gate.Sent(0);
            gate.Progress(49);
            Check(!gate.Expired(49) && gate.Expired(50), "Waiting for acknowledgement incorrectly extended timeout");
            gate.Finish();
            Check(!gate.Active && !gate.Acknowledge(1, request, 0, 51), "Cancellation retained the job");
            gate.Reset();
            Check(gate.Begin(1, Guid.NewGuid(), 1, out _) == RefreshState.Accepted, "World reset retained cooldown");
        });

        [UnitTest(name: "Refresh fragments roundtrip a record larger than one batch", category: "Sync")]
        public static UnitTestResult FragmentRoundtrip() => Run(() =>
        {
            byte[] source = Enumerable.Range(0, 140000).Select(i => (byte)(i % 251)).ToArray();
            var assembler = new RefreshRecordAssembler(); byte[] result = null;
            for (int offset = 0; offset < source.Length; offset += RefreshLimits.FragmentBytes)
            {
                byte[] part = source.Skip(offset).Take(RefreshLimits.FragmentBytes).ToArray();
                result = assembler.Append(RefreshRecordKind.Structure, source.Length, offset, part);
                if (offset + part.Length < source.Length) Check(result == null, "Record applied before its final fragment");
            }
            Check(result.SequenceEqual(source), "Fragment bytes changed");
            Check(assembler.Append(RefreshRecordKind.Logic, 1, 0, new byte[] { 9 })[0] == 9, "Assembler retained old record");
        });

        [UnitTest(name: "Refresh rejects missing, duplicate, mismatched and oversized fragments", category: "Sync")]
        public static UnitTestResult InvalidFragments() => Run(() =>
        {
            var assembler = new RefreshRecordAssembler();
            Invalid(() => assembler.Append(RefreshRecordKind.Terrain, 2, 1, new byte[] { 1 }));
            Invalid(() => assembler.Append(RefreshRecordKind.Terrain, RefreshLimits.RecordBytes + 1, 0, new byte[] { 1 }));
            Check(assembler.Append(RefreshRecordKind.Terrain, 2, 0, new byte[] { 1 }) == null, "Partial record completed");
            Invalid(() => assembler.Append(RefreshRecordKind.Terrain, 2, 0, new byte[] { 1 }));
            Invalid(() => assembler.Append(RefreshRecordKind.Logic, 2, 1, new byte[] { 2 }));
            assembler.Clear();
            Check(assembler.Append(RefreshRecordKind.Logic, 1, 0, new byte[] { 3 })[0] == 3, "Cancellation retained fragments");
        });

        [UnitTest(name: "Transport reassembly separates clients with colliding sequence IDs", category: "Transport")]
        public static UnitTestResult TransportSenderIsolation() => Run(() =>
        {
            var assembler = new TransportChunkAssembler();
            assembler.Add(1, 7, 1, 2, new byte[] { 2 }, 0);
            assembler.Add(2, 7, 0, 2, new byte[] { 9 }, 0);
            assembler.Add(1, 7, 1, 2, new byte[] { 2 }, 1); // Duplicate cannot count as a missing chunk.
            Check(assembler.Add(1, 7, 0, 2, new byte[] { 1 }, 2).SequenceEqual(new byte[] { 1, 2 }), "Clients mixed during reassembly");
            Check(assembler.Add(2, 7, 1, 2, new byte[] { 8 }, 2).SequenceEqual(new byte[] { 9, 8 }), "Second client's data was lost");
        });

        [UnitTest(name: "Transport reassembly expires and clears unfinished messages", category: "Transport")]
        public static UnitTestResult TransportCleanup() => Run(() =>
        {
            var assembler = new TransportChunkAssembler();
            assembler.Add(1, 1, 0, 2, new byte[] { 1 }, 0);
            Check(assembler.Add(1, 1, 1, 2, new byte[] { 2 }, 31) == null, "Expired data was replayed");
            assembler.Clear();
            Check(assembler.Add(1, 1, 0, 2, new byte[] { 3 }, 32) == null, "Clear retained an old chunk");
            Invalid(() => assembler.Add(1, 2, 2, 2, new byte[] { 1 }, 33));
        });

        [UnitTest(name: "Refresh payloads cannot serialize their transport sender identity", category: "Transport")]
        public static UnitTestResult SenderNotOnWire() => Run(() =>
        {
            var request = new ColonyRefreshRequestPacket { RequestId = Guid.NewGuid(), SenderId = 999 };
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) request.Serialize(writer);
            Check(stream.Length == 17, "Request unexpectedly includes a claimed sender");
            stream.Position = 0; var decoded = new ColonyRefreshRequestPacket();
            decoded.Deserialize(new BinaryReader(stream));
            Check(decoded.RequestId == request.RequestId && !decoded.SenderId.HasValue, "Payload supplied authenticated sender");
        });

        [UnitTest(name: "Refresh responses require the current request and authenticated host", category: "Transport")]
        public static UnitTestResult ResponseRouting() => Run(() =>
        {
            var request = Guid.NewGuid();
            Check(RefreshRouting.IsResponseFor(request, 7, request, 7), "Host response rejected");
            Check(!RefreshRouting.IsResponseFor(request, 7, request, 8), "Another player supplied host data");
            Check(!RefreshRouting.IsResponseFor(request, 7, request, null), "Unauthenticated response accepted");
            Check(!RefreshRouting.IsResponseFor(request, 7, Guid.NewGuid(), 7), "Expired request response accepted");
            Check(!RefreshRouting.IsResponseFor(Guid.Empty, 7, Guid.Empty, 7), "Response accepted with no active job");
        });

        [UnitTest(name: "Refresh batch wire size includes headers and rejects truncated data", category: "Sync")]
        public static UnitTestResult BatchWireLimit() => Run(() =>
        {
            var packet = new ColonyRefreshBatchPacket
            {
                RequestId = Guid.NewGuid(), Sequence = 1, Kind = RefreshRecordKind.Structure,
                RecordLength = RefreshLimits.RecordBytes, Data = new byte[RefreshLimits.FragmentBytes]
            };
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) packet.Serialize(writer);
            Check(stream.Length + sizeof(int) <= RefreshLimits.BatchBytes, "Packet header escaped the batch limit");
            stream.Position = 0; var decoded = new ColonyRefreshBatchPacket(); decoded.Deserialize(new BinaryReader(stream));
            Check(decoded.RecordLength == packet.RecordLength && decoded.Data.Length == packet.Data.Length, "Batch state changed");
            byte[] truncated = stream.ToArray().Take((int)stream.Length - 1).ToArray();
            Invalid(() => new ColonyRefreshBatchPacket().Deserialize(new BinaryReader(new MemoryStream(truncated))));
        });

        [UnitTest(name: "Building and automation packets preserve component identity and revisions", category: "Sync")]
        public static UnitTestResult BuildingWireRoundtrip() => Run(() =>
        {
            var structure = new StructureStatePacket
            {
                NetId = 21, Cell = 42, SyncerType = "BatteryStateSyncer", Revision = 99, Value = 250f,
                OptionalValues = new Dictionary<string, Variant> { ["stor"] = new byte[] { 1, 2, 3 } }
            };
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) structure.Serialize(writer);
            stream.Position = 0; var decoded = new StructureStatePacket(); decoded.Deserialize(new BinaryReader(stream));
            Check(decoded.SyncerType == structure.SyncerType && decoded.Revision == 99 && decoded.Value.Float == 250,
                "Component state or revision changed");
            Check(decoded.OptionalValues["stor"].ByteArray.SequenceEqual(new byte[] { 1, 2, 3 }), "Storage bytes changed");
            stream.SetLength(0); stream.Position = 0;
            var logic = new LogicStatePacket { NetId = 21, Cell = 42, Revision = 100, Value = true, IsActive = true };
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) logic.Serialize(writer);
            stream.Position = 0; var decodedLogic = new LogicStatePacket(); decodedLogic.Deserialize(new BinaryReader(stream));
            Check(decodedLogic.NetId == 21 && decodedLogic.Revision == 100 && decodedLogic.Value.Boolean && decodedLogic.IsActive,
                "Automation state or revision changed");
        });

        [UnitTest(name: "Building records reject trailing fields and truncated storage variants", category: "Sync")]
        public static UnitTestResult MalformedBuildingValues() => Run(() =>
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            { writer.Write(5); writer.Write(0); writer.Write((byte)1); }
            stream.Position = 0;
            Invalid(() => StatePacketValues.Read(new BinaryReader(stream)));
            stream.SetLength(0); stream.Position = 0;
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            { writer.Write((byte)Variant.TypeCode.ByteArray); writer.Write(3); writer.Write((byte)1); }
            stream.Position = 0;
            Invalid(() => Variant.Read(new BinaryReader(stream)));
            Invalid(() => Variant.Read(new BinaryReader(new MemoryStream(new byte[] { 255 }))));
        });

        [UnitTest(name: "Delayed refresh revisions cannot replace newer live state", category: "Sync")]
        public static UnitTestResult RevisionOrdering() => Run(() =>
        {
            var cache = new RevisionCache<int>();
            Check(cache.IsNewer(1, 10), "First state rejected"); cache.Record(1, 10);
            Check(!cache.IsNewer(1, 9) && !cache.IsNewer(1, 10), "Delayed/duplicate state accepted");
            Check(cache.IsNewer(1, 11) && cache.IsNewer(2, 9), "New state or independent cell rejected");
            Check(!cache.IsNewer(2, 0), "Unstamped update accepted"); cache.Clear();
            Check(cache.IsNewer(1, 1), "World reset retained revisions");
        });

        [UnitTest(name: "Terrain packet preserves capture revisions through compression", category: "Sync")]
        public static UnitTestResult TerrainRevisionRoundtrip() => Run(() =>
        {
            long revision = StateRevisions.Next();
            var packet = new WorldUpdatePacket();
            packet.Updates.Add(new WorldUpdatePacket.CellUpdate { Cell = 42, Revision = revision, Mass = 2, Temperature = 300, DiseaseIdx = 255 });
            long refreshRevision = StateRevisions.Next();
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) packet.Serialize(writer);
            stream.Position = 0; var decoded = new WorldUpdatePacket(); decoded.Deserialize(new BinaryReader(stream));
            Check(decoded.Updates[0].Revision == revision && revision < refreshRevision, "Batching assigned a newer revision to older data");
        });

        private static byte[] Blob(params StorageSnapshotEntry[] entries)
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            writer.Write(1000f); writer.Write(entries.Length);
            foreach (var e in entries)
            { writer.Write(e.PrefabHash); writer.Write(e.Mass); writer.Write(e.Temperature); writer.Write(e.DiseaseIdx); writer.Write(e.DiseaseCount); }
            return stream.ToArray();
        }

        [UnitTest(name: "Storage snapshot validates full and empty containers", category: "Sync")]
        public static UnitTestResult StorageParsing() => Run(() =>
        {
            var entries = StorageSnapshot.Parse(Blob(new StorageSnapshotEntry(10, 5, 300, 0, 100)));
            Check(entries.Count == 1 && entries[0].DiseaseCount == 100 && entries[0].Mass == 5, "Stored state changed");
            Check(StorageSnapshot.Parse(Blob()).Count == 0, "Empty container rejected");
        });

        [UnitTest(name: "Malformed storage is rejected before a reconciliation plan exists", category: "Sync")]
        public static UnitTestResult StorageMalformed() => Run(() =>
        {
            byte[] valid = Blob(new StorageSnapshotEntry(10, 5, 300, 255, 0));
            Invalid(() => StorageSnapshot.Parse(valid.Take(valid.Length - 1).ToArray()));
            Invalid(() => StorageSnapshot.Parse(valid.Concat(new byte[] { 0 }).ToArray()));
            Invalid(() => StorageSnapshot.Parse(Blob(new StorageSnapshotEntry(10, float.NaN, 300, 255, 0))));
            Invalid(() => StorageSnapshot.Parse(Blob(new StorageSnapshotEntry(10, 5, float.PositiveInfinity, 255, 0))));
            Invalid(() => StorageSnapshot.Parse(Blob(new StorageSnapshotEntry(10, 5, 300, 255, 10))));
            var negativeCount = Blob(); negativeCount[4] = 255; negativeCount[5] = 255; negativeCount[6] = 255; negativeCount[7] = 255;
            Invalid(() => StorageSnapshot.Parse(negativeCount));
        });

        [UnitTest(name: "Storage matching preserves order and excludes protected items", category: "Sync")]
        public static UnitTestResult StorageMatching() => Run(() =>
        {
            int?[] existing = { null, 10, 20, 10, null };
            var entries = StorageSnapshot.Parse(Blob(new StorageSnapshotEntry(10, 1, 300, 255, 0),
                new StorageSnapshotEntry(10, 2, 301, 255, 0), new StorageSnapshotEntry(30, 3, 302, 255, 0)));
            Check(StorageSnapshot.Match(existing, entries).SequenceEqual(new[] { 1, 3, -1 }), "Stack order/identity or protection changed");
        });

        [UnitTest(name: "Repeated storage reconciliation reuses existing stack identities", category: "Sync")]
        public static UnitTestResult StorageRepeatedRefresh() => Run(() =>
        {
            var entries = StorageSnapshot.Parse(Blob(new StorageSnapshotEntry(10, 1, 300, 255, 0), new StorageSnapshotEntry(20, 2, 301, 255, 0)));
            int?[] existing = { null, 10, 20 };
            var first = StorageSnapshot.Match(existing, entries);
            var second = StorageSnapshot.Match(existing, entries);
            Check(first.SequenceEqual(new[] { 1, 2 }) && second.SequenceEqual(first), "Repeated refresh would recreate matched objects");
            Check(StorageSnapshot.Match(existing, new List<StorageSnapshotEntry>()).Length == 0, "Empty snapshot matched protected items");
        });
    }
}
