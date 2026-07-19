#if DEBUG
using System.IO;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.World;
using Shared.Interfaces.Networking;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class SoakRawFenceTests
	{
		[UnitTest(name: "Soak raw fence carries the pre-keyframe hash", category: "Networking")]
		public static UnitTestResult RawFenceCarriesExactObservedHash()
		{
			var raw = new SoakHashReportPacket
			{
				RunId = 9,
				SampleId = 3,
				CompletedTicks = 5_400,
				Cycle = 12,
				CycleTime = 34.5f,
				StorageMembershipRecords = 7,
			};
				raw.StorageMembershipHash[0] = 41;
			raw.Lifecycle.UnassignedLiveCount = 2;
			var fence = RoundTrip(new SoakRawFencePacket
			{
				RunId = 9,
				SampleId = 3,
				CompletedTicks = 5_400,
				RepairSequenceCut = 17,
			});
			var ack = RoundTrip(new SoakRawFenceAckPacket
			{
				RunId = 9,
				SampleId = 3,
				CompletedTicks = 5_400,
				RepairSequenceCut = 17,
				RawObserved = raw,
			});

			if (fence is not IHostOnlyPacket || (object)ack is IHostOnlyPacket
			    || !OrderedReliableChannel.ShouldWrap(
				    fence, PacketSendMode.ReliableImmediate)
			    || ack.RepairSequenceCut != 17
			    || ack.RawObserved.Cycle != 12 || ack.RawObserved.CycleTime != 34.5f
			    || ack.RawObserved.StorageMembershipRecords != 7
			    || ack.RawObserved.Lifecycle.UnassignedLiveCount != 2
			    || ack.RawObserved.StorageMembershipHash[0] != 41)
				return UnitTestResult.Fail("Raw fence lost its causal cut or pre-keyframe hash");
			return UnitTestResult.Pass("Raw hash is captured before keyframe convergence");
		}

		private static T RoundTrip<T>(T source) where T : IPacket, new()
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(
				       stream, System.Text.Encoding.UTF8, leaveOpen: true))
				source.Serialize(writer);
			stream.Position = 0;
			var copy = new T();
			using var reader = new BinaryReader(stream);
			copy.Deserialize(reader);
			return copy;
		}
	}
}
#endif
