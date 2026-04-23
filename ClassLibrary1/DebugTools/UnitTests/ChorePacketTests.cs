using System.Collections.Generic;
using System.IO;
using System.Text;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.Chores;

namespace ONI_MP.DebugTools.UnitTests
{
    public static class ChorePacketTests
    {
        [UnitTest(name: "ChoreErrandsPacket roundtrip preserves current and upcoming", category: "Chores")]
        public static UnitTestResult RoundTripWithCurrent()
        {
            var input = new ChoreErrandsPacket
            {
                DupeNetId = 42,
                Entries =
                [
                    new()
                    {
                        ChoreTypeId = "Dig", TargetCell = 50290, TargetLabel = "Natural Tile", PriorityClass = 0,
                        Priority = 5, PersonalPriority = 1, IsCurrent = true
                    },
                    new()
                    {
                        ChoreTypeId = "Cook", TargetCell = 12345, TargetLabel = "Microbe Musher", PriorityClass = 0,
                        Priority = 5, PersonalPriority = 1, IsCurrent = false
                    },
                    new()
                    {
                        ChoreTypeId = "Build", TargetCell = 67890, TargetLabel = "Ladder", PriorityClass = 0,
                        Priority = 5, PersonalPriority = 1, IsCurrent = false
                    }
                ]
            };

            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
                input.Serialize(w);

            ms.Position = 0;
            var output = new ChoreErrandsPacket();
            using (var r = new BinaryReader(ms, Encoding.UTF8, true))
                output.Deserialize(r);

            if (output.DupeNetId != 42) return UnitTestResult.Fail("DupeNetId mismatch");
            if (output.Entries.Count != 3) return UnitTestResult.Fail($"Expected 3, got {output.Entries.Count}");
            if (!output.Entries[0].IsCurrent) return UnitTestResult.Fail("First entry should be current");
            if (output.Entries[1].TargetLabel != "Microbe Musher") return UnitTestResult.Fail("Entry 1 target label mismatch");
            if (output.Entries[2].ChoreTypeId != "Build") return UnitTestResult.Fail("Entry 2 chore type mismatch");

            return UnitTestResult.Pass("Roundtripped current + upcoming");
        }

        [UnitTest(name: "ChoreErrandsPacket clamps oversize list", category: "Chores")]
        public static UnitTestResult ClampsOversize()
        {
            var input = new ChoreErrandsPacket { DupeNetId = 1 };
            for (int i = 0; i < ChoreErrandsPacket.MaxEntries + 5; i++)
                input.Entries.Add(new ErrandEntry { ChoreTypeId = "Dig", TargetCell = i, TargetLabel = "x" });

            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
                input.Serialize(w);

            ms.Position = 0;
            var output = new ChoreErrandsPacket();
            using (var r = new BinaryReader(ms, Encoding.UTF8, true))
                output.Deserialize(r);

            if (output.Entries.Count != ChoreErrandsPacket.MaxEntries)
                return UnitTestResult.Fail($"Expected {ChoreErrandsPacket.MaxEntries}, got {output.Entries.Count}");

            return UnitTestResult.Pass("Oversize list clamped");
        }

        [UnitTest(name: "ChoreErrandsSubscribePacket roundtrip", category: "Chores")]
        public static UnitTestResult SubscribeRoundTrip()
        {
            var input = new ChoreErrandsSubscribePacket { DupeNetId = 99, Subscribe = true };

            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
                input.Serialize(w);

            ms.Position = 0;
            var output = new ChoreErrandsSubscribePacket();
            using (var r = new BinaryReader(ms, Encoding.UTF8, true))
                output.Deserialize(r);

            if (output.DupeNetId != 99) return UnitTestResult.Fail("DupeNetId mismatch");
            if (output.Subscribe != true) return UnitTestResult.Fail("Subscribe flag mismatch");

            return UnitTestResult.Pass("Subscribe packet roundtripped");
        }

        [UnitTest(name: "ClientReceiver_ChoreErrands splits current from upcoming", category: "Chores")]
        public static UnitTestResult ReceiverSplitsCurrent()
        {
            var receiver = new ClientReceiver_ChoreErrands();
            receiver.Apply([
                new() { ChoreTypeId = "Cook", IsCurrent = false },
                new() { ChoreTypeId = "Dig", IsCurrent = true },
                new() { ChoreTypeId = "Build", IsCurrent = false }
            ]);

            if (!receiver.Current.HasValue) return UnitTestResult.Fail("Current not set");
            if (receiver.Current.Value.ChoreTypeId != "Dig") return UnitTestResult.Fail($"Current was '{receiver.Current.Value.ChoreTypeId}'");
            if (receiver.Upcoming.Count != 2) return UnitTestResult.Fail($"Upcoming count {receiver.Upcoming.Count}");

            return UnitTestResult.Pass("Receiver split current from upcoming");
        }
    }
}
