#if DEBUG
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Transport;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class TransportQueueRetentionTests
	{
		private sealed class AcceptingSender : TransportPacketSender
		{
			public override bool SendPacket(
				object conn, IPacket packet,
				PacketSendMode sendType = PacketSendMode.ReliableImmediate) => true;
		}

		[UnitTest(name: "Queued reliable packets survive transport refusal", category: "Networking")]
		public static UnitTestResult ReliableQueueRetainsFailedHead()
		{
			if (TransportPacketSender.ShouldDequeueAfterSend(false, PacketSendMode.Reliable)
			    || TransportPacketSender.ShouldDequeueAfterSend(false, PacketSendMode.ReliableImmediate))
			{
				return UnitTestResult.Fail("A failed reliable send was removed from the queue");
			}
			if (!TransportPacketSender.ShouldDequeueAfterSend(true, PacketSendMode.Reliable)
			    || !TransportPacketSender.ShouldDequeueAfterSend(false, PacketSendMode.Unreliable))
			{
				return UnitTestResult.Fail("Successful or stale-unreliable queue progress was blocked");
			}
			return UnitTestResult.Pass("Reliable queue heads remain until the transport accepts them");
		}

		[UnitTest(name: "Connection replacement drops connection-scoped queues", category: "Networking")]
		public static UnitTestResult ReplacedConnectionDropsPendingQueues()
		{
			bool originalQueueSetting = Configuration.Instance.EnablePacketQueue;
			TransportPacketSender originalSender = NetworkConfig.TransportPacketSender;
			var sender = new AcceptingSender();
			object connection = new();
			try
			{
				Configuration.Instance.EnablePacketQueue = true;
				NetworkConfig.TransportPacketSender = sender;
				PacketSender.ResetSessionState();
				sender.SendToConnection(connection, new WorldCyclePacket(), PacketSendMode.Reliable);
				PacketSender.AppendPendingBulkPacket(
					connection, new StorageItemPacket(), new StorageItemPacket());
				var player = new MultiplayerPlayer(77);
				long generation = player.BeginConnection(connection);
				if (!player.EndConnection(connection, generation))
					return UnitTestResult.Fail("Current connection could not close");
				if (sender.PendingCountForTests(connection) != 0
				    || PacketSender.PendingBulkCountForTests(connection) != 0)
					return UnitTestResult.Fail("Old connection retained reliable or bulk packets");
				return UnitTestResult.Pass("Connection-scoped queues cannot leak into a new snapshot epoch");
			}
			finally
			{
				PacketSender.ResetSessionState();
				NetworkConfig.TransportPacketSender = originalSender;
				Configuration.Instance.EnablePacketQueue = originalQueueSetting;
			}
		}
	}
}
#endif
