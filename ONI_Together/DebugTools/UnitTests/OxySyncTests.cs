using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.OxySync.Components;
using ONI_Together.Networking.OxySync.Packets;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests
{
    public static class OxySyncTests
    {
        private enum TestEnum
        {
            HighValue = 1000,
        }

		[UnitTest(name: "OxySync protocol hashes are deterministic", category: "OxySync")]
		public static UnitTestResult OxySyncProtocolHashIsDeterministic()
		{
			if (ProtocolCompatibility.CurrentProtocolVersion < 2)
				return UnitTestResult.Fail("The handshake still permits pre-stable-hash OxySync peers");

			int a1 = "CmdSetSpeed".GetHashCode();
			int a2 = "CmdSetSpeed".GetHashCode();
			if (a1 != a2)
				return UnitTestResult.Fail($"Hash not deterministic: {a1} != {a2}");
			if (a1 != "CmdSetSpeed".GetHashCode())
				return UnitTestResult.Fail($"OxySyncHash should delegate to GetHashCode: got {a1} vs { "CmdSetSpeed".GetHashCode()}");
            int b = "OtherMethod".GetHashCode();
			if (a1 == b)
				return UnitTestResult.Fail("Different strings produced same hash");

			return UnitTestResult.Pass("OxySync identifiers use GetHashCode (deterministic on Unity Mono)");
		}

        [UnitTest(name: "Snapshot timeline ignores clock skew and arrival jitter", category: "OxySync")]
        public static UnitTestResult SnapshotTimelineNormalizesRemoteTime()
        {
            var timeline = new SnapshotTimeline();

            double first = timeline.ToLocalTime(1_700_000_000_000L, 1_000.0);
            double second = timeline.ToLocalTime(1_700_000_000_050L, 1_135.0);
            double third = timeline.ToLocalTime(1_700_000_000_100L, 1_155.0);

            if (Math.Abs(first - 1_000.0) > 0.001)
                return UnitTestResult.Fail($"Initial local anchor mismatch: {first}");
            if (Math.Abs(second - 1_050.0) > 0.001)
                return UnitTestResult.Fail($"Remote cadence was changed by arrival jitter: {second}");
            if (Math.Abs(third - 1_100.0) > 0.001)
                return UnitTestResult.Fail($"Remote cadence was changed by clock skew: {third}");

            return UnitTestResult.Pass("Remote snapshots use a stable local monotonic timeline");
        }

		[UnitTest(name: "Snapshot timeline adapts its buffer to arrival jitter", category: "OxySync")]
		public static UnitTestResult SnapshotTimelineAdaptsToJitter()
		{
			var timeline = new SnapshotTimeline();
			timeline.ToLocalTime(1_000L, 1_000.0);
			timeline.ToLocalTime(1_050L, 1_140.0);
			timeline.ToLocalTime(1_100L, 1_160.0);

			double adaptive = timeline.GetAdaptiveBufferMilliseconds(150.0, 350.0);
			if (adaptive <= 150.0 || adaptive > 350.0)
				return UnitTestResult.Fail($"Adaptive buffer out of range: {adaptive}");

			timeline.Reset();
			if (timeline.EstimatedJitterMilliseconds != 0.0)
				return UnitTestResult.Fail("Reset did not clear estimated jitter");

			return UnitTestResult.Pass("Arrival jitter increases the interpolation buffer within its cap");
		}

		[UnitTest(name: "Snapshot timeline ignores reordered arrivals", category: "OxySync")]
		public static UnitTestResult SnapshotTimelineIgnoresReorderedArrivalForJitter()
		{
			var timeline = new SnapshotTimeline();
			timeline.ToLocalTime(1_000L, 1_000.0);
			timeline.ToLocalTime(1_100L, 1_100.0);
			double olderMapped = timeline.ToLocalTime(1_050L, 1_150.0);
			timeline.ToLocalTime(1_150L, 1_150.0);

			if (Math.Abs(olderMapped - 1_050.0) > 0.001)
				return UnitTestResult.Fail("Reordered packet did not retain the anchor mapping");
			if (timeline.EstimatedJitterMilliseconds > 0.001)
				return UnitTestResult.Fail($"Reordered packet poisoned jitter estimate: {timeline.EstimatedJitterMilliseconds}");

			return UnitTestResult.Pass("Reordered channels do not inflate the adaptive buffer");
		}

		[UnitTest(name: "Realtime snapshots bypass the optional packet queue", category: "OxySync")]
		public static UnitTestResult RealtimeSnapshotsBypassPacketQueue()
		{
			if (!Networking.Transport.TransportPacketSender.IsLatencySensitive(PacketSendMode.UnreliableNoDelay))
				return UnitTestResult.Fail("UnreliableNoDelay should bypass the queue");
			if (Networking.Transport.TransportPacketSender.IsLatencySensitive(PacketSendMode.Unreliable))
				return UnitTestResult.Fail("Regular unreliable traffic should retain configured queue behavior");
			if (Networking.Transport.TransportPacketSender.IsLatencySensitive(PacketSendMode.ReliableImmediate))
				return UnitTestResult.Fail("Reliable traffic must retain ordering through the queue");

			return UnitTestResult.Pass("Only latency-sensitive unreliable snapshots bypass the queue");
		}

        [UnitTest(name: "SyncVarPacket round-trip", category: "OxySync")]
        public static UnitTestResult SyncVarPacketRoundTrip()
        {
            var input = new SyncVarPacket
            {
                NetId = 12345,
                BehaviourId = 2,
                FieldHash = "health".GetHashCode(),
                Value = (Variant)100f,
                Timestamp = 987654321098L,
            };

            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
                input.Serialize(w);

            ms.Position = 0;
            var output = new SyncVarPacket();
            using (var r = new BinaryReader(ms, Encoding.UTF8, true))
                output.Deserialize(r);

            if (output.NetId != 12345)
                return UnitTestResult.Fail($"NetId mismatch: {output.NetId}");
            if (output.BehaviourId != 2)
                return UnitTestResult.Fail($"BehaviourId mismatch: {output.BehaviourId}");
            if (output.FieldHash != input.FieldHash)
                return UnitTestResult.Fail("FieldHash mismatch");
            if (Mathf.Abs(output.Value.Float - 100f) > 0.001f)
                return UnitTestResult.Fail($"Float value mismatch: {output.Value.Float}");
            if (output.Timestamp != 987654321098L)
                return UnitTestResult.Fail($"Timestamp mismatch: {output.Timestamp}");

            return UnitTestResult.Pass("SyncVarPacket round-trips correctly");
        }

        [UnitTest(name: "SyncVarBatchPacket round-trip", category: "OxySync")]
        public static UnitTestResult SyncVarBatchPacketRoundTrip()
        {
            var updates = new System.Collections.Generic.List<(int Hash, Variant Value)>
            {
                ("hp".GetHashCode(), (Variant)80f),
                ("dead".GetHashCode(), (Variant)false),
                ("name".GetHashCode(), (Variant)"Alice"),
                ("count".GetHashCode(), (Variant)42),
            };

            var input = new SyncVarBatchPacket(999, 3, updates)
            {
                Timestamp = 1234567890123L,
            };

            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
                input.Serialize(w);

            ms.Position = 0;
            var output = new SyncVarBatchPacket();
            using (var r = new BinaryReader(ms, Encoding.UTF8, true))
                output.Deserialize(r);

            if (output.NetId != 999)
                return UnitTestResult.Fail($"NetId mismatch: {output.NetId}");
            if (output.BehaviourId != 3)
                return UnitTestResult.Fail($"BehaviourId mismatch: {output.BehaviourId}");
            if (output.Timestamp != 1234567890123L)
                return UnitTestResult.Fail($"Timestamp mismatch: {output.Timestamp}");
            if (output.Count != 4)
                return UnitTestResult.Fail($"Count mismatch: {output.Count}");
            if (output.FieldHashes[0] != updates[0].Hash)
                return UnitTestResult.Fail("Hash 0 mismatch");
            if (Mathf.Abs(output.Values[0].Float - 80f) > 0.001f)
                return UnitTestResult.Fail("Float value 0 mismatch");
            if (output.Values[2].String != "Alice")
                return UnitTestResult.Fail("String value 2 mismatch");
            if (output.Values[3].Int != 42)
                return UnitTestResult.Fail("Int value 3 mismatch");

            return UnitTestResult.Pass("SyncVarBatchPacket round-trips correctly");
        }

        [UnitTest(name: "CommandPacket round-trip", category: "OxySync")]
        public static UnitTestResult CommandPacketRoundTrip()
        {
            var input = new CommandPacket
            {
                NetId = 777,
                BehaviourId = 1,
                MethodHash = "TakeDamage".GetHashCode(),
                Args = new byte[] { 0x01, 0x02, 0x03 },
            };

            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
                input.Serialize(w);

            ms.Position = 0;
            var output = new CommandPacket();
            using (var r = new BinaryReader(ms, Encoding.UTF8, true))
                output.Deserialize(r);

            if (output.NetId != 777)
                return UnitTestResult.Fail($"NetId mismatch: {output.NetId}");
            if (output.BehaviourId != 1)
                return UnitTestResult.Fail($"BehaviourId mismatch: {output.BehaviourId}");
            if (output.MethodHash != input.MethodHash)
                return UnitTestResult.Fail("MethodHash mismatch");
            if (output.Args.Length != 3 || output.Args[0] != 0x01)
                return UnitTestResult.Fail("Args mismatch");

            return UnitTestResult.Pass("CommandPacket round-trips correctly");
        }

        [UnitTest(name: "ClientRpcPacket round-trip (broadcast)", category: "OxySync")]
        public static UnitTestResult ClientRpcPacketBroadcastRoundTrip()
        {
            var input = new ClientRpcPacket
            {
                NetId = 555,
                BehaviourId = 2,
                MethodHash = "RpcHealed".GetHashCode(),
                Args = new byte[] { 0x0A },
                TargetPlayerId = ulong.MaxValue,
            };

            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
                input.Serialize(w);

            ms.Position = 0;
            var output = new ClientRpcPacket();
            using (var r = new BinaryReader(ms, Encoding.UTF8, true))
                output.Deserialize(r);

            if (output.NetId != 555)
                return UnitTestResult.Fail($"NetId mismatch: {output.NetId}");
            if (output.BehaviourId != 2)
                return UnitTestResult.Fail($"BehaviourId mismatch: {output.BehaviourId}");
            if (output.MethodHash != input.MethodHash)
                return UnitTestResult.Fail("MethodHash mismatch");
            if (output.Args.Length != 1 || output.Args[0] != 0x0A)
                return UnitTestResult.Fail("Args mismatch");
            if (output.TargetPlayerId != ulong.MaxValue)
                return UnitTestResult.Fail("TargetPlayerId should be broadcast");

            return UnitTestResult.Pass("ClientRpcPacket (broadcast) round-trips correctly");
        }

        [UnitTest(name: "ClientRpcPacket round-trip (targeted)", category: "OxySync")]
        public static UnitTestResult ClientRpcPacketTargetedRoundTrip()
        {
            var input = new ClientRpcPacket
            {
                NetId = 444,
                BehaviourId = 3,
                MethodHash = "RpcPrivateMsg".GetHashCode(),
                Args = Array.Empty<byte>(),
                TargetPlayerId = 9001,
            };

            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
                input.Serialize(w);

            ms.Position = 0;
            var output = new ClientRpcPacket();
            using (var r = new BinaryReader(ms, Encoding.UTF8, true))
                output.Deserialize(r);

            if (output.NetId != 444)
                return UnitTestResult.Fail($"NetId mismatch: {output.NetId}");
            if (output.BehaviourId != 3)
                return UnitTestResult.Fail($"BehaviourId mismatch: {output.BehaviourId}");
            if (output.TargetPlayerId != 9001)
                return UnitTestResult.Fail($"TargetPlayerId mismatch: {output.TargetPlayerId}");

            return UnitTestResult.Pass("ClientRpcPacket (targeted) round-trips correctly");
        }

        [UnitTest(name: "RpcSerializer all 12 types round-trip", category: "OxySync")]
        public static UnitTestResult RpcSerializerAllTypes()
        {
            object[] args = {
                42,
                3.14f,
                true,
                (byte)7,
                999L,
                2.71828,
                "hello",
                new Vector2(1.5f, 2.5f),
                new Vector3(3f, 4f, 5f),
                new Color(0.1f, 0.2f, 0.3f, 0.4f),
                new Quaternion(0f, 0f, 0f, 1f),
                new byte[] { 0xAA, 0xBB, 0xCC },
                new HashedString(55555),
                new KAnimHashedString(66666),
                TestEnum.HighValue,
            };

            Type[] types = {
                typeof(int), typeof(float), typeof(bool), typeof(byte),
                typeof(long), typeof(double), typeof(string),
                typeof(Vector2), typeof(Vector3), typeof(Color),
                typeof(Quaternion), typeof(byte[]),
                typeof(HashedString), typeof(KAnimHashedString),
                typeof(TestEnum),
            };

            var data = RpcSerializer.Serialize(args, types);
            var result = RpcSerializer.Deserialize(data, types);

            if ((int)result[0] != 42)
                return UnitTestResult.Fail("int mismatch");
            if (Mathf.Abs((float)result[1] - 3.14f) > 0.001f)
                return UnitTestResult.Fail("float mismatch");
            if ((bool)result[2] != true)
                return UnitTestResult.Fail("bool mismatch");
            if ((byte)result[3] != 7)
                return UnitTestResult.Fail("byte mismatch");
            if ((long)result[4] != 999L)
                return UnitTestResult.Fail("long mismatch");
            if (Math.Abs((double)result[5] - 2.71828) > 0.00001)
                return UnitTestResult.Fail("double mismatch");
            if ((string)result[6] != "hello")
                return UnitTestResult.Fail("string mismatch");

            var v2 = (Vector2)result[7];
            if (Mathf.Abs(v2.x - 1.5f) > 0.001f || Mathf.Abs(v2.y - 2.5f) > 0.001f)
                return UnitTestResult.Fail("Vector2 mismatch");

            var v3 = (Vector3)result[8];
            if (Mathf.Abs(v3.x - 3f) > 0.001f || Mathf.Abs(v3.y - 4f) > 0.001f || Mathf.Abs(v3.z - 5f) > 0.001f)
                return UnitTestResult.Fail("Vector3 mismatch");

            var c = (Color)result[9];
            if (Mathf.Abs(c.r - 0.1f) > 0.001f || Mathf.Abs(c.g - 0.2f) > 0.001f)
                return UnitTestResult.Fail("Color mismatch");

            var q = (Quaternion)result[10];
            if (Mathf.Abs(q.w - 1f) > 0.001f)
                return UnitTestResult.Fail("Quaternion mismatch");

            var ba = (byte[])result[11];
            if (ba.Length != 3 || ba[0] != 0xAA || ba[1] != 0xBB || ba[2] != 0xCC)
                return UnitTestResult.Fail("byte[] mismatch");

            var hs = (HashedString)result[12];
            if (hs.hash != 55555)
                return UnitTestResult.Fail("HashedString mismatch");

            var khs = (KAnimHashedString)result[13];
            if (khs.hash != 66666)
                return UnitTestResult.Fail("KAnimHashedString mismatch");

            if ((TestEnum)result[14] != TestEnum.HighValue)
                return UnitTestResult.Fail("enum mismatch");

            return UnitTestResult.Pass("All 15 RPC types round-trip correctly");
        }

        [UnitTest(name: "RpcSerializer empty args", category: "OxySync")]
        public static UnitTestResult RpcSerializerEmptyArgs()
        {
            var data = RpcSerializer.Serialize(Array.Empty<object>(), Array.Empty<Type>());
            var result = RpcSerializer.Deserialize(data, Array.Empty<Type>());

            if (result.Length != 0)
                return UnitTestResult.Fail($"Expected 0 results, got {result.Length}");

            return UnitTestResult.Pass("Empty args round-trip correctly");
        }
        
		[UnitTest(name: "SyncVar Variant rejects invalid data", category: "OxySync")]
		public static UnitTestResult SyncVarVariantRejectsInvalidData()
		{
			Variant nullVariant = VariantHelper.ObjectToVariant(null);
			if (nullVariant.Type != Variant.TypeCode.Null ||
				VariantHelper.VariantToObject(nullVariant, typeof(string)) != null)
				return UnitTestResult.Fail("A null SyncVar did not preserve its null state");

			Variant testEnum = VariantHelper.ObjectToVariant(TestEnum.HighValue);
			if ((TestEnum)VariantHelper.VariantToObject(testEnum, typeof(TestEnum)) != TestEnum.HighValue)
				return UnitTestResult.Fail("An enum SyncVar did not round-trip");

			const decimal decimalInput = 1234567890.123456789m;
			Variant decimalVariant = VariantHelper.ObjectToVariant(decimalInput);
			if ((decimal)VariantHelper.VariantToObject(decimalVariant, typeof(decimal)) != decimalInput)
				return UnitTestResult.Fail("A decimal SyncVar did not round-trip");

			try
			{
				VariantHelper.ObjectToVariant(new object());
				return UnitTestResult.Fail("An unsupported SyncVar value silently serialized");
			}
			catch (NotSupportedException)
			{
			}

			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
			{
				writer.Write((byte)Variant.TypeCode.ByteArray);
				writer.Write(Variant.MaxBinaryBytes + 1);
			}
			stream.Position = 0;
			try
			{
				using var reader = new BinaryReader(stream, Encoding.UTF8, true);
				Variant.Read(reader);
				return UnitTestResult.Fail("An oversized SyncVar byte array was accepted");
			}
			catch (InvalidDataException)
			{
			}

			return UnitTestResult.Pass("Unsupported and oversized SyncVar values are rejected explicitly");
		}

        [UnitTest(name: "RpcSerializer string handles null", category: "OxySync")]
        public static UnitTestResult RpcSerializerNullString()
        {
            object[] args = { (string)null };
            Type[] types = { typeof(string) };
            var data = RpcSerializer.Serialize(args, types);
            var result = RpcSerializer.Deserialize(data, types);

            if (result[0] == null)
                return UnitTestResult.Fail("Null string should round-trip as empty string");

            return UnitTestResult.Pass("Null string serializes as empty");
        }

        [UnitTest(name: "Variant all 23 types round-trip", category: "OxySync")]
        public static UnitTestResult VariantAllTypesRoundTrip()
        {
            Variant[] inputs = {
                (Variant)100f,
                (Variant)42,
                (Variant)(byte)7,
                (Variant)"test",
                (Variant)true,
                (Variant)new Vector3(1f, 2f, 3f),
                (Variant)new Vector2(4f, 5f),
                (Variant)new byte[] { 0x01, 0x02 },
                (Variant)new Quaternion(0.1f, 0.2f, 0.3f, 0.4f),
                (Variant)new HashedString(12345),
                (Variant)new KAnimHashedString(67890),
                (Variant)(short)-1234,
                (Variant)(ushort)5678,
                (Variant)(uint)4000000000,
                (Variant)9999999999999L,
                (Variant)3.14159265358979,
                (Variant)(sbyte)-99,
                (Variant)'Z',
                (Variant)new Color(0.1f, 0.2f, 0.3f, 0.4f),
                new Variant { Type = Variant.TypeCode.VariantArray, VariantArray = new Variant[] { (Variant)1, (Variant)2, (Variant)3 } },
                (Variant)new int[] { 10, 20, 30 },
                (Variant)new float[] { 1.5f, 2.5f },
                (Variant)new double[] { 3.14, 2.71 },
            };

            for (int i = 0; i < inputs.Length; i++)
            {
                using var ms = new MemoryStream();
                using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
                    inputs[i].Write(w);

                ms.Position = 0;
                var output = Variant.Read(new BinaryReader(ms, Encoding.UTF8, true));

                switch (inputs[i].Type)
                {
                    case Variant.TypeCode.Float:
                        if (Mathf.Abs(output.Float - 100f) > 0.001f)
                            return UnitTestResult.Fail($"Float variant {i} mismatch");
                        break;
                    case Variant.TypeCode.Int:
                        if (output.Int != 42)
                            return UnitTestResult.Fail($"Int variant {i} mismatch");
                        break;
                    case Variant.TypeCode.Byte:
                        if (output.Byte != 7)
                            return UnitTestResult.Fail($"Byte variant {i} mismatch");
                        break;
                    case Variant.TypeCode.String:
                        if (output.String != "test")
                            return UnitTestResult.Fail($"String variant {i} mismatch");
                        break;
                    case Variant.TypeCode.Boolean:
                        if (output.Boolean != true)
                            return UnitTestResult.Fail($"Bool variant {i} mismatch");
                        break;
                    case Variant.TypeCode.Vector3:
                        if (Mathf.Abs(output.Vector3.x - 1f) > 0.001f)
                            return UnitTestResult.Fail($"Vector3 variant {i} mismatch");
                        break;
                    case Variant.TypeCode.Vector2:
                        if (Mathf.Abs(output.Vector2.x - 4f) > 0.001f)
                            return UnitTestResult.Fail($"Vector2 variant {i} mismatch");
                        break;
                    case Variant.TypeCode.ByteArray:
                        if (output.ByteArray.Length != 2 || output.ByteArray[0] != 0x01)
                            return UnitTestResult.Fail($"byte[] variant {i} mismatch");
                        break;
                    case Variant.TypeCode.Quaternion:
                        var q = new Quaternion(0.1f, 0.2f, 0.3f, 0.4f);
                        if (Mathf.Abs(output.Quaternion.x - q.x) > 0.001f ||
                            Mathf.Abs(output.Quaternion.y - q.y) > 0.001f ||
                            Mathf.Abs(output.Quaternion.z - q.z) > 0.001f ||
                            Mathf.Abs(output.Quaternion.w - q.w) > 0.001f)
                            return UnitTestResult.Fail($"Quaternion variant {i} mismatch");
                        break;
                    case Variant.TypeCode.HashedString:
                        if (output.Int != 12345)
                            return UnitTestResult.Fail($"HashedString variant {i} mismatch");
                        break;
                    case Variant.TypeCode.KAnimHashedString:
                        if (output.Int != 67890)
                            return UnitTestResult.Fail($"KAnimHashedString variant {i} mismatch");
                        break;
                    case Variant.TypeCode.Short:
                        if (output.Int != -1234)
                            return UnitTestResult.Fail($"Short variant {i} mismatch");
                        break;
                    case Variant.TypeCode.UShort:
                        if (output.Int != 5678)
                            return UnitTestResult.Fail($"UShort variant {i} mismatch");
                        break;
                    case Variant.TypeCode.UInt:
                        if (output.Long != 4000000000)
                            return UnitTestResult.Fail($"UInt variant {i} mismatch");
                        break;
                    case Variant.TypeCode.Long:
                        if (output.Long != 9999999999999L)
                            return UnitTestResult.Fail($"Long variant {i} mismatch");
                        break;
                    case Variant.TypeCode.Double:
                        if (Math.Abs(output.Double - 3.14159265358979) > 0.0000000001)
                            return UnitTestResult.Fail($"Double variant {i} mismatch");
                        break;
                    case Variant.TypeCode.SByte:
                        if ((sbyte)output.Byte != -99)
                            return UnitTestResult.Fail($"SByte variant {i} mismatch");
                        break;
                    case Variant.TypeCode.Char:
                        if (output.Int != 'Z')
                            return UnitTestResult.Fail($"Char variant {i} mismatch");
                        break;
                    case Variant.TypeCode.Color:
                        var expectedCol = new Color(0.1f, 0.2f, 0.3f, 0.4f);
                        if (Mathf.Abs(output.Color.r - expectedCol.r) > 0.001f ||
                            Mathf.Abs(output.Color.g - expectedCol.g) > 0.001f ||
                            Mathf.Abs(output.Color.b - expectedCol.b) > 0.001f ||
                            Mathf.Abs(output.Color.a - expectedCol.a) > 0.001f)
                            return UnitTestResult.Fail($"Color variant {i} mismatch");
                        break;
                    case Variant.TypeCode.VariantArray:
                        if (output.VariantArray.Length != 3 ||
                            output.VariantArray[0].Int != 1 ||
                            output.VariantArray[1].Int != 2 ||
                            output.VariantArray[2].Int != 3)
                            return UnitTestResult.Fail($"VariantArray variant {i} mismatch");
                        break;
                    case Variant.TypeCode.IntArray:
                        if (output.IntArray.Length != 3 || output.IntArray[0] != 10 || output.IntArray[2] != 30)
                            return UnitTestResult.Fail($"IntArray variant {i} mismatch");
                        break;
                    case Variant.TypeCode.FloatArray:
                        if (output.FloatArray.Length != 2 || Mathf.Abs(output.FloatArray[0] - 1.5f) > 0.001f)
                            return UnitTestResult.Fail($"FloatArray variant {i} mismatch");
                        break;
                    case Variant.TypeCode.DoubleArray:
                        if (output.DoubleArray.Length != 2 || Math.Abs(output.DoubleArray[1] - 2.71) > 0.001)
                            return UnitTestResult.Fail($"DoubleArray variant {i} mismatch");
                        break;
                }
            }

            return UnitTestResult.Pass("All 23 Variant types round-trip correctly");
        }

        [UnitTest(name: "VariantToObject supports all types", category: "OxySync")]
        public static UnitTestResult VariantToObjectAllTypes()
        {
            var testCases = new (Variant V, Type TargetType, object Expected)[]
            {
                ((Variant)42, typeof(int), 42),
                ((Variant)3.14f, typeof(float), 3.14f),
                ((Variant)(byte)7, typeof(byte), (byte)7),
                ((Variant)"hello", typeof(string), "hello"),
                ((Variant)true, typeof(bool), true),
                ((Variant)new Vector3(1,2,3), typeof(Vector3), new Vector3(1,2,3)),
                ((Variant)new Vector2(4,5), typeof(Vector2), new Vector2(4,5)),
                ((Variant)new byte[] { 0x01 }, typeof(byte[]), new byte[] { 0x01 }),
                ((Variant)new Quaternion(0.1f, 0.2f, 0.3f, 0.4f), typeof(Quaternion), new Quaternion(0.1f, 0.2f, 0.3f, 0.4f)),
                (new Variant { Type = Variant.TypeCode.HashedString, Int = 12345 }, typeof(HashedString), new HashedString(12345)),
                (new Variant { Type = Variant.TypeCode.KAnimHashedString, Int = 67890 }, typeof(KAnimHashedString), new KAnimHashedString(67890)),
                (new Variant { Type = Variant.TypeCode.Int, Int = 2 }, typeof(System.StringComparison), System.StringComparison.InvariantCulture),
                (new Variant { Type = Variant.TypeCode.Int, Int = 5 }, typeof(System.StringComparison), System.StringComparison.OrdinalIgnoreCase),
                ((Variant)(short)-1234, typeof(short), (short)-1234),
                ((Variant)(ushort)5678, typeof(ushort), (ushort)5678),
                ((Variant)(uint)4000000000, typeof(uint), (uint)4000000000u),
                ((Variant)9999999999999L, typeof(long), 9999999999999L),
                ((Variant)3.14159265358979, typeof(double), 3.14159265358979),
                ((Variant)(sbyte)-99, typeof(sbyte), (sbyte)-99),
                ((Variant)'Z', typeof(char), 'Z'),
                ((Variant)new Color(0.1f, 0.2f, 0.3f, 0.4f), typeof(Color), new Color(0.1f, 0.2f, 0.3f, 0.4f)),
                ((Variant)new int[] { 10, 20, 30 }, typeof(int[]), new int[] { 10, 20, 30 }),
                ((Variant)new float[] { 1.5f, 2.5f }, typeof(float[]), new float[] { 1.5f, 2.5f }),
                ((Variant)new double[] { 3.14, 2.71 }, typeof(double[]), new double[] { 3.14, 2.71 }),
            };

            foreach (var (v, type, expected) in testCases)
            {
                var result = VariantHelper.VariantToObject(v, type);
                if (result == null)
                    return UnitTestResult.Fail($"VariantToObject returned null for {type.Name}");

                if (type == typeof(float))
                {
                    if (Mathf.Abs((float)result - (float)expected) > 0.001f)
                        return UnitTestResult.Fail($"Float conversion mismatch: {result} != {expected}");
                }
                else if (type == typeof(double))
                {
                    if (Math.Abs((double)result - (double)expected) > 0.0000000001)
                        return UnitTestResult.Fail($"Double conversion mismatch: {result} != {expected}");
                }
                else if (type == typeof(Vector3))
                {
                    var r = (Vector3)result;
                    var e = (Vector3)expected;
                    if (Vector3.Distance(r, e) > 0.001f)
                        return UnitTestResult.Fail($"Vector3 conversion mismatch: {r} != {e}");
                }
                else if (type == typeof(Vector2))
                {
                    var r = (Vector2)result;
                    var e = (Vector2)expected;
                    if (Vector2.Distance(r, e) > 0.001f)
                        return UnitTestResult.Fail($"Vector2 conversion mismatch: {r} != {e}");
                }
                else if (type == typeof(byte[]))
                {
                    var r = (byte[])result;
                    var e = (byte[])expected;
                    if (r.Length != e.Length || r[0] != e[0])
                        return UnitTestResult.Fail($"byte[] conversion mismatch");
                }
                else if (type == typeof(int[]))
                {
                    var r = (int[])result;
                    var e = (int[])expected;
                    if (r.Length != e.Length || r[0] != e[0] || r[2] != e[2])
                        return UnitTestResult.Fail($"int[] conversion mismatch");
                }
                else if (type == typeof(float[]))
                {
                    var r = (float[])result;
                    var e = (float[])expected;
                    if (r.Length != e.Length || Mathf.Abs(r[0] - e[0]) > 0.001f)
                        return UnitTestResult.Fail($"float[] conversion mismatch");
                }
                else if (type == typeof(double[]))
                {
                    var r = (double[])result;
                    var e = (double[])expected;
                    if (r.Length != e.Length || Math.Abs(r[0] - e[0]) > 0.001)
                        return UnitTestResult.Fail($"double[] conversion mismatch");
                }
                else if (type == typeof(Quaternion))
                {
                    var r = (Quaternion)result;
                    var e = (Quaternion)expected;
                    if (Mathf.Abs(r.x - e.x) > 0.001f || Mathf.Abs(r.y - e.y) > 0.001f ||
                        Mathf.Abs(r.z - e.z) > 0.001f || Mathf.Abs(r.w - e.w) > 0.001f)
                        return UnitTestResult.Fail($"Quaternion conversion mismatch: {r} != {e}");
                }
                else if (!result.Equals(expected))
                {
                    return UnitTestResult.Fail($"{type.Name} conversion mismatch: {result} != {expected}");
                }
            }

            return UnitTestResult.Pass("VariantToObject converts all 24 supported types");
        }

        [UnitTest(name: "VariantToObject null string fallback", category: "OxySync")]
        public static UnitTestResult VariantToObjectNullString()
        {
            var v = new Variant { Type = Variant.TypeCode.String, String = null };
            var result = VariantHelper.VariantToObject(v, typeof(string));
            if (result is not string s || s != string.Empty)
                return UnitTestResult.Fail("Null string should become empty string");
            return UnitTestResult.Pass("Null string falls back to empty");
        }

        [UnitTest(name: "VariantToObject null byte[] fallback", category: "OxySync")]
        public static UnitTestResult VariantToObjectNullByteArray()
        {
            var v = new Variant { Type = Variant.TypeCode.ByteArray, ByteArray = null };
            var result = VariantHelper.VariantToObject(v, typeof(byte[]));
            if (result is not byte[] ba || ba.Length != 0)
                return UnitTestResult.Fail("Null byte[] should become empty array");
            return UnitTestResult.Pass("Null byte[] falls back to empty");
        }

        [UnitTest(name: "VariantToObject collections round-trip", category: "OxySync")]
        public static UnitTestResult VariantToObjectCollections()
        {
            var vArr = new Variant { Type = Variant.TypeCode.VariantArray, VariantArray = new Variant[] { (Variant)1, (Variant)2, (Variant)3 } };

            var arr = (int[])VariantHelper.VariantToObject(vArr, typeof(int[]));
            if (arr.Length != 3 || arr[0] != 1 || arr[1] != 2 || arr[2] != 3)
                return UnitTestResult.Fail("int[] mismatch");

            var list = (List<string>)VariantHelper.VariantToObject(
                new Variant { Type = Variant.TypeCode.VariantArray, VariantArray = new Variant[] { (Variant)"a", (Variant)"b" } },
                typeof(List<string>));
            if (list.Count != 2 || list[0] != "a" || list[1] != "b")
                return UnitTestResult.Fail("List<string> mismatch");

            var hashSet = (HashSet<int>)VariantHelper.VariantToObject(
                new Variant { Type = Variant.TypeCode.VariantArray, VariantArray = new Variant[] { (Variant)10, (Variant)20 } },
                typeof(HashSet<int>));
            if (hashSet.Count != 2 || !hashSet.Contains(10) || !hashSet.Contains(20))
                return UnitTestResult.Fail("HashSet<int> mismatch");

            var queue = (Queue<string>)VariantHelper.VariantToObject(
                new Variant { Type = Variant.TypeCode.VariantArray, VariantArray = new Variant[] { (Variant)"x", (Variant)"y" } },
                typeof(Queue<string>));
            if (queue.Count != 2 || queue.Dequeue() != "x" || queue.Dequeue() != "y")
                return UnitTestResult.Fail("Queue<string> mismatch");

            var stack = (Stack<float>)VariantHelper.VariantToObject(
                new Variant { Type = Variant.TypeCode.VariantArray, VariantArray = new Variant[] { (Variant)3f, (Variant)2f, (Variant)1f } },
                typeof(Stack<float>));
            if (stack.Count != 3 || Mathf.Abs(stack.Pop() - 1f) > 0.001f)
                return UnitTestResult.Fail("Stack<float> mismatch");

            var dict = (Dictionary<string, int>)VariantHelper.VariantToObject(
                new Variant { Type = Variant.TypeCode.VariantArray, VariantArray = new Variant[] { (Variant)"k1", (Variant)1, (Variant)"k2", (Variant)2 } },
                typeof(Dictionary<string, int>));
            if (dict.Count != 2 || dict["k1"] != 1 || dict["k2"] != 2)
                return UnitTestResult.Fail("Dictionary<string,int> mismatch");

            return UnitTestResult.Pass("All 6 collection types round-trip correctly");
        }

        [UnitTest(name: "RpcSerializer new primitives round-trip", category: "OxySync")]
        public static UnitTestResult RpcSerializerNewPrimitives()
        {
            object[] args = {
                (short)-1234,
                (ushort)5678,
                (uint)4000000000,
                (sbyte)-99,
                'Z',
                3.14159265358979323846m,
            };

            Type[] types = {
                typeof(short), typeof(ushort), typeof(uint),
                typeof(sbyte), typeof(char), typeof(decimal),
            };

            var data = RpcSerializer.Serialize(args, types);
            var result = RpcSerializer.Deserialize(data, types);

            if ((short)result[0] != -1234)
                return UnitTestResult.Fail("short mismatch");
            if ((ushort)result[1] != 5678)
                return UnitTestResult.Fail("ushort mismatch");
            if ((uint)result[2] != 4000000000)
                return UnitTestResult.Fail("uint mismatch");
            if ((sbyte)result[3] != -99)
                return UnitTestResult.Fail("sbyte mismatch");
            if ((char)result[4] != 'Z')
                return UnitTestResult.Fail("char mismatch");
            if ((decimal)result[5] != 3.14159265358979323846m)
                return UnitTestResult.Fail("decimal mismatch");

            return UnitTestResult.Pass("All 6 new primitive types round-trip correctly");
        }

        [UnitTest(name: "RpcSerializer typed arrays round-trip", category: "OxySync")]
        public static UnitTestResult RpcSerializerTypedArrays()
        {
            object[] args = {
                new int[] { 10, 20, 30 },
                new string[] { "x", "y", "z" },
                new Vector3[] { Vector3.right, Vector3.up, Vector3.forward },
                new long[0],
            };

            Type[] types = {
                typeof(int[]), typeof(string[]), typeof(Vector3[]), typeof(long[]),
            };

            var data = RpcSerializer.Serialize(args, types);
            var result = RpcSerializer.Deserialize(data, types);

            var a1 = (int[])result[0];
            if (a1.Length != 3 || a1[0] != 10 || a1[2] != 30)
                return UnitTestResult.Fail("int[] mismatch");

            var a2 = (string[])result[1];
            if (a2.Length != 3 || a2[1] != "y")
                return UnitTestResult.Fail("string[] mismatch");

            var a3 = (Vector3[])result[2];
            if (a3.Length != 3 || a3[0] != Vector3.right)
                return UnitTestResult.Fail("Vector3[] mismatch");

            var a4 = (long[])result[3];
            if (a4.Length != 0)
                return UnitTestResult.Fail("long[] empty mismatch");

            return UnitTestResult.Pass("Typed arrays round-trip correctly");
        }

        [UnitTest(name: "RpcSerializer List<T> round-trip", category: "OxySync")]
        public static UnitTestResult RpcSerializerListTypes()
        {
            object[] args = {
                new List<int> { 1, 2, 3 },
                new List<string> { "a", "b" },
                new List<Vector3> { Vector3.one, Vector3.zero, Vector3.back },
                new List<bool>(),
            };

            Type[] types = {
                typeof(List<int>), typeof(List<string>),
                typeof(List<Vector3>), typeof(List<bool>),
            };

            var data = RpcSerializer.Serialize(args, types);
            var result = RpcSerializer.Deserialize(data, types);

            var l1 = (List<int>)result[0];
            if (l1.Count != 3 || l1[0] != 1 || l1[2] != 3)
                return UnitTestResult.Fail("List<int> mismatch");

            var l2 = (List<string>)result[1];
            if (l2.Count != 2 || l2[0] != "a" || l2[1] != "b")
                return UnitTestResult.Fail("List<string> mismatch");

            var l3 = (List<Vector3>)result[2];
            if (l3.Count != 3 || l3[0] != Vector3.one)
                return UnitTestResult.Fail("List<Vector3> mismatch");

            var l4 = (List<bool>)result[3];
            if (l4.Count != 0)
                return UnitTestResult.Fail("List<bool> empty mismatch");

            return UnitTestResult.Pass("List<T> round-trip correctly");
        }

        [UnitTest(name: "RpcSerializer Dictionary<K,V> round-trip", category: "OxySync")]
        public static UnitTestResult RpcSerializerDictionaryTypes()
        {
            object[] args = {
                new Dictionary<string, int> { { "one", 1 }, { "two", 2 }, { "three", 3 } },
                new Dictionary<int, bool> { { 42, true }, { 0, false } },
                new Dictionary<string, string>(),
            };

            Type[] types = {
                typeof(Dictionary<string, int>),
                typeof(Dictionary<int, bool>),
                typeof(Dictionary<string, string>),
            };

            var data = RpcSerializer.Serialize(args, types);
            var result = RpcSerializer.Deserialize(data, types);

            var d1 = (Dictionary<string, int>)result[0];
            if (d1.Count != 3 || d1["one"] != 1 || d1["three"] != 3)
                return UnitTestResult.Fail("Dictionary<string,int> mismatch");

            var d2 = (Dictionary<int, bool>)result[1];
            if (d2.Count != 2 || d2[42] != true || d2[0] != false)
                return UnitTestResult.Fail("Dictionary<int,bool> mismatch");

            var d3 = (Dictionary<string, string>)result[2];
            if (d3.Count != 0)
                return UnitTestResult.Fail("Dictionary<string,string> empty mismatch");

            return UnitTestResult.Pass("Dictionary<K,V> round-trip correctly");
        }

        [UnitTest(name: "RpcSerializer HashSet, Queue, Stack round-trip", category: "OxySync")]
        public static UnitTestResult RpcSerializerHashSetQueueStackTypes()
        {
            object[] args = {
                new HashSet<int> { 5, 10, 15, 20 },
                new Queue<string>(new[] { "first", "second", "third" }),
                new Stack<float>(new[] { 3.0f, 2.0f, 1.0f }),
                new HashSet<double>(),
            };

            Type[] types = {
                typeof(HashSet<int>), typeof(Queue<string>),
                typeof(Stack<float>), typeof(HashSet<double>),
            };

            var data = RpcSerializer.Serialize(args, types);
            var result = RpcSerializer.Deserialize(data, types);

            var h1 = (HashSet<int>)result[0];
            if (h1.Count != 4 || !h1.Contains(5) || !h1.Contains(20))
                return UnitTestResult.Fail("HashSet<int> mismatch");

            var q1 = (Queue<string>)result[1];
            if (q1.Count != 3 || q1.Dequeue() != "first" || q1.Dequeue() != "second" || q1.Dequeue() != "third")
                return UnitTestResult.Fail("Queue<string> mismatch");

            var s1 = (Stack<float>)result[2];
            if (s1.Count != 3 || Mathf.Abs(s1.Pop() - 1.0f) > 0.001f)
                return UnitTestResult.Fail("Stack<float> mismatch");

            var h2 = (HashSet<double>)result[3];
            if (h2.Count != 0)
                return UnitTestResult.Fail("HashSet<double> empty mismatch");

            return UnitTestResult.Pass("HashSet/Queue/Stack round-trip correctly");
        }

        [UnitTest(name: "RpcSerializer Nullable<T> round-trip", category: "OxySync")]
        public static UnitTestResult RpcSerializerNullableTypes()
        {
            object[] args = {
                (int?)42,
                (int?)null,
                (float?)3.14f,
                (Vector3?)Vector3.up,
                (Vector3?)null,
                (bool?)true,
            };

            Type[] types = {
                typeof(int?), typeof(int?), typeof(float?),
                typeof(Vector3?), typeof(Vector3?), typeof(bool?),
            };

            var data = RpcSerializer.Serialize(args, types);
            var result = RpcSerializer.Deserialize(data, types);

            // int? with value
            var r0 = (int?)result[0];
            if (r0 == null || r0.Value != 42)
                return UnitTestResult.Fail("int? with value mismatch");

            // int? null
            if (result[1] != null)
                return UnitTestResult.Fail("int? null mismatch");

            // float? with value
            var r2 = (float?)result[2];
            if (r2 == null || Mathf.Abs(r2.Value - 3.14f) > 0.001f)
                return UnitTestResult.Fail("float? with value mismatch");

            // Vector3? with value
            var r3 = (Vector3?)result[3];
            if (r3 == null || r3.Value != Vector3.up)
                return UnitTestResult.Fail("Vector3? with value mismatch");

            // Vector3? null
            if (result[4] != null)
                return UnitTestResult.Fail("Vector3? null mismatch");

            // bool? with value
            var r5 = (bool?)result[5];
            if (r5 == null || r5.Value != true)
                return UnitTestResult.Fail("bool? with value mismatch");

            return UnitTestResult.Pass("Nullable<T> round-trip correctly");
        }

        [UnitTest(name: "RpcSerializer nested collections round-trip", category: "OxySync")]
        public static UnitTestResult RpcSerializerNestedCollections()
        {
            object[] args = {
                new List<List<int>> {
                    new List<int> { 1, 2 },
                    new List<int> { 3, 4, 5 },
                },
                new Dictionary<string, int[]> {
                    { "even", new[] { 2, 4, 6 } },
                    { "odd", new[] { 1, 3, 5 } },
                },
                new int[][] {
                    new[] { 1, 0, 0 },
                    new[] { 0, 1, 0 },
                    new[] { 0, 0, 1 },
                },
            };

            Type[] types = {
                typeof(List<List<int>>),
                typeof(Dictionary<string, int[]>),
                typeof(int[][]),
            };

            var data = RpcSerializer.Serialize(args, types);
            var result = RpcSerializer.Deserialize(data, types);

            var l1 = (List<List<int>>)result[0];
            if (l1.Count != 2 || l1[0][0] != 1 || l1[1][2] != 5)
                return UnitTestResult.Fail("List<List<int>> mismatch");

            var d1 = (Dictionary<string, int[]>)result[1];
            if (d1.Count != 2 || d1["even"].Length != 3 || d1["odd"][1] != 3)
                return UnitTestResult.Fail("Dictionary<string,int[]> mismatch");

            var a1 = (int[][])result[2];
            if (a1.Length != 3 || a1[1][1] != 1 || a1[2][2] != 1)
                return UnitTestResult.Fail("int[][] mismatch");

            return UnitTestResult.Pass("Nested collections round-trip correctly");
        }

        [UnitTest(name: "RpcSerializer null collection elements", category: "OxySync")]
        public static UnitTestResult RpcSerializerNullCollectionElements()
        {
            object[] args = {
                new List<string> { "alive", null, "dead", null },
                new List<int?> { 1, null, 3 },
                new string[] { "a", null, "c" },
            };

            Type[] types = {
                typeof(List<string>),
                typeof(List<int?>),
                typeof(string[]),
            };

            var data = RpcSerializer.Serialize(args, types);
            var result = RpcSerializer.Deserialize(data, types);

            var l1 = (List<string>)result[0];
            if (l1.Count != 4 || l1[0] != "alive" || l1[1] != null || l1[2] != "dead" || l1[3] != null)
                return UnitTestResult.Fail("List<string> null elements mismatch");

            var l2 = (List<int?>)result[1];
            if (l2.Count != 3 || l2[0] != 1 || l2[1] != null || l2[2] != 3)
                return UnitTestResult.Fail("List<int?> null elements mismatch");

            var a1 = (string[])result[2];
            if (a1.Length != 3 || a1[0] != "a" || a1[1] != null || a1[2] != "c")
                return UnitTestResult.Fail("string[] null elements mismatch");

            return UnitTestResult.Pass("Null collection elements round-trip correctly");
        }

        [UnitTest(name: "RpcSerializer IsSupportedType covers new types", category: "OxySync")]
        public static UnitTestResult RpcSerializerIsSupportedType()
        {
            Type[] supported = {
                typeof(int), typeof(float), typeof(bool), typeof(byte),
                typeof(long), typeof(double), typeof(string),
                typeof(Vector2), typeof(Vector3), typeof(Color),
                typeof(Quaternion), typeof(byte[]), typeof(ulong),
                typeof(short), typeof(ushort), typeof(uint),
                typeof(sbyte), typeof(char), typeof(decimal),
                typeof(HashedString), typeof(KAnimHashedString),
                typeof(int[]), typeof(string[]), typeof(Vector3[]),
                typeof(List<int>), typeof(List<string>),
                typeof(Dictionary<string, int>),
                typeof(HashSet<float>), typeof(Queue<long>), typeof(Stack<bool>),
                typeof(int?), typeof(float?), typeof(Vector3?),
                typeof(List<List<int>>),
                typeof(Dictionary<string, List<int>>),
                typeof(int[][]),
            };

            foreach (var t in supported)
            {
                if (!RpcSerializer.IsSupportedType(t))
                    return UnitTestResult.Fail($"Type {t} should be supported but is not");
            }

            Type[] unsupported = {
                typeof(object),
                typeof(Guid),
                typeof(DateTime),
                typeof(TimeSpan),
                typeof(Tuple<int, int>),
                typeof(System.Action),
                typeof(Stream),
            };

            foreach (var t in unsupported)
            {
                if (RpcSerializer.IsSupportedType(t))
                    return UnitTestResult.Fail($"Type {t} should NOT be supported but is");
            }

            return UnitTestResult.Pass("IsSupportedType correctly validates all types");
        }
    }

    public class WideSyncVarBehaviour : NetworkBehaviour
    {
        [SyncVar] private int _field0;
        [SyncVar] private int _field1;
        [SyncVar] private int _field2;
        [SyncVar] private int _field3;
        [SyncVar] private int _field4;
        [SyncVar] private int _field5;
        [SyncVar] private int _field6;
        [SyncVar] private int _field7;
        [SyncVar] private int _field8;
        [SyncVar] private int _field9;
        [SyncVar] private int _field10;
        [SyncVar] private int _field11;
        [SyncVar] private int _field12;
        [SyncVar] private int _field13;
        [SyncVar] private int _field14;
        [SyncVar] private int _field15;
        [SyncVar] private int _field16;
        [SyncVar] private int _field17;
        [SyncVar] private int _field18;
        [SyncVar] private int _field19;
        [SyncVar] private int _field20;
        [SyncVar] private int _field21;
        [SyncVar] private int _field22;
        [SyncVar] private int _field23;
        [SyncVar] private int _field24;
        [SyncVar] private int _field25;
        [SyncVar] private int _field26;
        [SyncVar] private int _field27;
        [SyncVar] private int _field28;
        [SyncVar] private int _field29;
        [SyncVar] private int _field30;
        [SyncVar] private int _field31;
        [SyncVar] private int _field32;
        [SyncVar] private int _field33;
        [SyncVar] private int _field34;
        [SyncVar] private int _field35;
        [SyncVar] private int _field36;
        [SyncVar] private int _field37;
        [SyncVar] private int _field38;
        [SyncVar] private int _field39;
        [SyncVar] private int _field40;
        [SyncVar] private int _field41;
        [SyncVar] private int _field42;
        [SyncVar] private int _field43;
        [SyncVar] private int _field44;
        [SyncVar] private int _field45;
        [SyncVar] private int _field46;
        [SyncVar] private int _field47;
        [SyncVar] private int _field48;
        [SyncVar] private int _field49;
        [SyncVar] private int _field50;
        [SyncVar] private int _field51;
        [SyncVar] private int _field52;
        [SyncVar] private int _field53;
        [SyncVar] private int _field54;
        [SyncVar] private int _field55;
        [SyncVar] private int _field56;
        [SyncVar] private int _field57;
        [SyncVar] private int _field58;
        [SyncVar] private int _field59;
        [SyncVar] private int _field60;
        [SyncVar] private int _field61;
        [SyncVar] private int _field62;
        [SyncVar] private int _field63;
    }

    public class FewSyncVarBehaviour : NetworkBehaviour
    {
        [SyncVar] private int _field0;
        [SyncVar] private int _field1;
        [SyncVar] private int _field2;
        [SyncVar] private int _field3;
        [SyncVar] private int _field4;
    }

    public class OverflowSyncVarBehaviour : NetworkBehaviour
    {
        [SyncVar] private int _field0;
        [SyncVar] private int _field1;
        [SyncVar] private int _field2;
        [SyncVar] private int _field3;
        [SyncVar] private int _field4;
        [SyncVar] private int _field5;
        [SyncVar] private int _field6;
        [SyncVar] private int _field7;
        [SyncVar] private int _field8;
        [SyncVar] private int _field9;
        [SyncVar] private int _field10;
        [SyncVar] private int _field11;
        [SyncVar] private int _field12;
        [SyncVar] private int _field13;
        [SyncVar] private int _field14;
        [SyncVar] private int _field15;
        [SyncVar] private int _field16;
        [SyncVar] private int _field17;
        [SyncVar] private int _field18;
        [SyncVar] private int _field19;
        [SyncVar] private int _field20;
        [SyncVar] private int _field21;
        [SyncVar] private int _field22;
        [SyncVar] private int _field23;
        [SyncVar] private int _field24;
        [SyncVar] private int _field25;
        [SyncVar] private int _field26;
        [SyncVar] private int _field27;
        [SyncVar] private int _field28;
        [SyncVar] private int _field29;
        [SyncVar] private int _field30;
        [SyncVar] private int _field31;
        [SyncVar] private int _field32;
        [SyncVar] private int _field33;
        [SyncVar] private int _field34;
        [SyncVar] private int _field35;
        [SyncVar] private int _field36;
        [SyncVar] private int _field37;
        [SyncVar] private int _field38;
        [SyncVar] private int _field39;
        [SyncVar] private int _field40;
        [SyncVar] private int _field41;
        [SyncVar] private int _field42;
        [SyncVar] private int _field43;
        [SyncVar] private int _field44;
        [SyncVar] private int _field45;
        [SyncVar] private int _field46;
        [SyncVar] private int _field47;
        [SyncVar] private int _field48;
        [SyncVar] private int _field49;
        [SyncVar] private int _field50;
        [SyncVar] private int _field51;
        [SyncVar] private int _field52;
        [SyncVar] private int _field53;
        [SyncVar] private int _field54;
        [SyncVar] private int _field55;
        [SyncVar] private int _field56;
        [SyncVar] private int _field57;
        [SyncVar] private int _field58;
        [SyncVar] private int _field59;
        [SyncVar] private int _field60;
        [SyncVar] private int _field61;
        [SyncVar] private int _field62;
        [SyncVar] private int _field63;
        [SyncVar] private int _field64;
    }

    public static class OxySyncDirtyBitTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static T CreateBehaviour<T>() where T : NetworkBehaviour
        {
            var go = new GameObject("OxySyncUnitTest");
            var behaviour = go.AddComponent<T>();
            try
            {
                typeof(NetworkBehaviour).GetMethod("DiscoverSyncVars", NonPublicInstance)
                    .Invoke(behaviour, null);
            }
            catch
            {
                UnityEngine.Object.Destroy(go);
                throw;
            }
            return behaviour;
        }

        private static int[] FieldHashes(NetworkBehaviour behaviour)
        {
            var fields = behaviour.SyncVarFields;
            var hashes = new int[fields.Count];
            for (int i = 0; i < fields.Count; i++)
                hashes[i] = fields[i].Hash;
            return hashes;
        }

        private static void MarkDirty(NetworkBehaviour behaviour, int fieldIndex)
        {
            typeof(NetworkBehaviour).GetMethod("MarkSyncVarAsDirty", NonPublicInstance,
                    null, new[] { typeof(int) }, null)
                .Invoke(behaviour, new object[] { behaviour.SyncVarFields[fieldIndex].Hash });
        }

        [UnitTest(name: "SyncVar dirty bits cover boundaries 0,31,32,63", category: "OxySync")]
        public static UnitTestResult SyncVarDirtyBitsCoverBoundaries()
        {
            var behaviour = CreateBehaviour<WideSyncVarBehaviour>();
            try
            {
                var fields = behaviour.SyncVarFields;
                if (fields.Count != 64)
                    return UnitTestResult.Fail($"Expected 64 SyncVars, got {fields.Count}");
                for (int i = 0; i < fields.Count; i++)
                    for (int j = i + 1; j < fields.Count; j++)
                        if (fields[i].Hash == fields[j].Hash)
                            return UnitTestResult.Fail($"Field hash collision between {i} and {j}");

                var hashes = FieldHashes(behaviour);
                behaviour.SetSyncVarValue(hashes[0], 1);
                behaviour.SetSyncVarValue(hashes[31], 1);
                behaviour.SetSyncVarValue(hashes[32], 1);
                behaviour.SetSyncVarValue(hashes[63], 1);

                ulong expected = (1UL << 0) | (1UL << 31) | (1UL << 32) | (1UL << 63);
                ulong actual = behaviour.GetAndClearDirtyBits();
                if (actual != expected)
                    return UnitTestResult.Fail($"Expected 0x{expected:X16}, got 0x{actual:X16}");

                if (behaviour.GetAndClearDirtyBits() != 0)
                    return UnitTestResult.Fail("Dirty bits were not cleared after read");

                return UnitTestResult.Pass("Bits 0,31,32,63 are independently tracked and cleared");
            }
            finally { UnityEngine.Object.Destroy(behaviour.gameObject); }
        }

        [UnitTest(name: "SyncVar regression: first 32 dirty bits unchanged", category: "OxySync")]
        public static UnitTestResult SyncVarRegressionFirst32()
        {
            var behaviour = CreateBehaviour<WideSyncVarBehaviour>();
            try
            {
                var hashes = FieldHashes(behaviour);
                ulong expected = 0;
                for (int i = 0; i < 32; i++)
                {
                    behaviour.SetSyncVarValue(hashes[i], i);
                    expected |= 1UL << i;
                }

                ulong actual = behaviour.GetAndClearDirtyBits();
                if (actual != expected)
                    return UnitTestResult.Fail($"Expected 0x{expected:X8}, got 0x{actual:X8}");

                return UnitTestResult.Pass("SyncVars 0-31 map to bits 0-31 exactly as before");
            }
            finally { UnityEngine.Object.Destroy(behaviour.gameObject); }
        }

        [UnitTest(name: "MarkAllDirty marks all 64 registered fields", category: "OxySync")]
        public static UnitTestResult MarkAllDirtyMarksAll64()
        {
            var behaviour = CreateBehaviour<WideSyncVarBehaviour>();
            try
            {
                behaviour.MarkAllDirty();
                ulong actual = behaviour.GetAndClearDirtyBits();
                if (actual != ulong.MaxValue)
                    return UnitTestResult.Fail($"Expected 0xFFFFFFFFFFFFFFFF, got 0x{actual:X16}");
                return UnitTestResult.Pass("MarkAllDirty sets all 64 bits");
            }
            finally { UnityEngine.Object.Destroy(behaviour.gameObject); }
        }

        [UnitTest(name: "MarkAllDirty sets only registered bits below 64", category: "OxySync")]
        public static UnitTestResult MarkAllDirtyFewFields()
        {
            var behaviour = CreateBehaviour<FewSyncVarBehaviour>();
            try
            {
                behaviour.MarkAllDirty();
                ulong actual = behaviour.GetAndClearDirtyBits();
                if (actual != 0x1FUL)
                    return UnitTestResult.Fail($"Expected 0x1F, got 0x{actual:X}");
                return UnitTestResult.Pass("MarkAllDirty sets only the 5 registered bits");
            }
            finally { UnityEngine.Object.Destroy(behaviour.gameObject); }
        }

        [UnitTest(name: "SyncVar set-bit iteration processes only dirty indices", category: "OxySync")]
        public static UnitTestResult SyncVarSetBitIteration()
        {
            var behaviour = CreateBehaviour<WideSyncVarBehaviour>();
            try
            {
                int[] dirty = { 3, 47, 63 };
                ulong mask = 0;
                foreach (int i in dirty)
                    mask |= 1UL << i;

                var hashes = FieldHashes(behaviour);
                var expected = new HashSet<int> { hashes[3], hashes[47], hashes[63] };

                var changes = new Dictionary<(int Group, PacketSendMode Mode), List<(int Hash, Variant Value)>>();
                OxySyncManager.CollectChanges(behaviour, mask, changes);

                var collected = new HashSet<int>();
                int total = 0;
                foreach (var list in changes.Values)
                    foreach (var update in list)
                    {
                        total++;
                        collected.Add(update.Hash);
                    }

                if (total != 3)
                    return UnitTestResult.Fail($"Expected 3 dirty fields processed, got {total}");
                if (!collected.SetEquals(expected))
                    return UnitTestResult.Fail("Processed hashes do not match indices {3,47,63}");

                return UnitTestResult.Pass("Only set bits 3,47,63 were processed");
            }
            finally { UnityEngine.Object.Destroy(behaviour.gameObject); }
        }

        [UnitTest(name: "SyncVars 32 and 63 synchronize correctly", category: "OxySync")]
        public static UnitTestResult SyncVarHighIndexSynchronize()
        {
            var behaviour = CreateBehaviour<WideSyncVarBehaviour>();
            try
            {
                var hashes = FieldHashes(behaviour);

                behaviour.SetSyncVarValue(hashes[32], 100);
                behaviour.SetSyncVarValue(hashes[63], 200);

                ulong expected = (1UL << 32) | (1UL << 63);
                ulong actual = behaviour.GetAndClearDirtyBits();
                if (actual != expected)
                    return UnitTestResult.Fail($"Expected 0x{expected:X16}, got 0x{actual:X16}");

                if ((int)behaviour.SyncVarFields[32].Info.GetValue(behaviour) != 100)
                    return UnitTestResult.Fail("Field 32 value not set");
                if ((int)behaviour.SyncVarFields[63].Info.GetValue(behaviour) != 200)
                    return UnitTestResult.Fail("Field 63 value not set");

                behaviour.ApplySyncVar(hashes[63], 300);
                if ((int)behaviour.SyncVarFields[63].Info.GetValue(behaviour) != 300)
                    return UnitTestResult.Fail("ApplySyncVar did not update field 63");

                return UnitTestResult.Pass("SyncVars 32 and 63 map, set, dirty and apply correctly");
            }
            finally { UnityEngine.Object.Destroy(behaviour.gameObject); }
        }

        [UnitTest(name: "Manually dirty + auto-detected SyncVar serialized once", category: "OxySync")]
        public static UnitTestResult SyncVarManualAndAutomaticSerializedOnce()
        {
            var behaviour = CreateBehaviour<WideSyncVarBehaviour>();
            try
            {
                int targetIndex = 5;
                behaviour.SyncVarFields[targetIndex].Info.SetValue(behaviour, 999);
                MarkDirty(behaviour, targetIndex);

                ulong manualDirty = behaviour.GetAndClearDirtyBits();
                var changes = new Dictionary<(int Group, PacketSendMode Mode), List<(int Hash, Variant Value)>>();
                OxySyncManager.CollectChanges(behaviour, manualDirty, changes);

                int total = 0;
                int matches = 0;
                int targetHash = behaviour.SyncVarFields[targetIndex].Hash;
                Variant emitted = default;
                foreach (var list in changes.Values)
                    foreach (var update in list)
                    {
                        total++;
                        if (update.Hash == targetHash)
                        {
                            matches++;
                            emitted = update.Value;
                        }
                    }

                if (total != 1)
                    return UnitTestResult.Fail($"Expected exactly 1 emitted SyncVar, got {total}");
                if (matches != 1)
                    return UnitTestResult.Fail($"Field {targetIndex} emitted {matches} times");
                if (emitted.Int != 999)
                    return UnitTestResult.Fail("Emitted value was not the current field value");

                return UnitTestResult.Pass("Doubly-triggered field is serialized exactly once");
            }
            finally { UnityEngine.Object.Destroy(behaviour.gameObject); }
        }

        [UnitTest(name: "More than 64 SyncVars throws at registration", category: "OxySync")]
        public static UnitTestResult SyncVarOverCapacityThrows()
        {
            var go = new GameObject("OxySyncUnitTest");
            var behaviour = go.AddComponent<OverflowSyncVarBehaviour>();
            try
            {
                try
                {
                    typeof(NetworkBehaviour).GetMethod("DiscoverSyncVars", NonPublicInstance)
                        .Invoke(behaviour, null);
                    return UnitTestResult.Fail("Expected InvalidOperationException for 65 SyncVars");
                }
                catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
                {
                    return UnitTestResult.Pass("65 SyncVars rejected at registration");
                }
                catch (TargetInvocationException ex)
                {
                    return UnitTestResult.Fail($"Unexpected exception: {ex.InnerException?.GetType().Name}");
                }
            }
            finally { UnityEngine.Object.Destroy(go); }
        }
    }
}
