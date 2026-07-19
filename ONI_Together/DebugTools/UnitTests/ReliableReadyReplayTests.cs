#if DEBUG
using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.States;
using ONI_Together.Networking.Transport;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class ReliableReadyReplayTests
	{
		public sealed class ReplayProbePacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
		{
			internal static int Applied;
			internal int Value;
			public void Serialize(BinaryWriter writer) => writer.Write(Value);
			public void Deserialize(BinaryReader reader) => Value = reader.ReadInt32();
			public void OnDispatched()
			{
				Applied++;
				if (Value == 2)
					throw new InvalidDataException("Synthetic replay failure");
			}
		}

		private sealed class RecordingSender : TransportPacketSender
		{
			internal readonly List<IPacket> Packets = new();
			public override bool SendPacket(object conn, IPacket packet,
				PacketSendMode sendType = PacketSendMode.ReliableImmediate)
			{
				Packets.Add(packet);
				return true;
			}
		}

		[UnitTest(name: "Ready replay commits only after the receiver applies and returns its ACK", category: "Networking")]
		public static UnitTestResult ReadyReplayUsesReceiverApplicationAck()
			=> Run(throwSecond: false);

		[UnitTest(name: "Rejected replay inner withholds ACK and aborts pending Ready", category: "Networking")]
		public static UnitTestResult RejectedReplayWithholdsAckAndReady()
			=> Run(throwSecond: true);

		private static UnitTestResult Run(bool throwSecond)
		{
			PacketRegistry.TryRegister(typeof(ReplayProbePacket));
			TransportPacketSender originalSender = NetworkConfig.TransportPacketSender;
			bool originalQueue = Configuration.Instance.EnablePacketQueue;
			bool originalHost = MultiplayerSession.IsHost;
			ulong originalHostId = MultiplayerSession.HostUserID;
			bool originalSession = MultiplayerSession.InSession;
			var originalPlayers = new Dictionary<ulong, MultiplayerPlayer>(MultiplayerSession.ConnectedPlayers);
			var sender = new RecordingSender();
			var hostConnection = new object();
			try
			{
				Configuration.Instance.EnablePacketQueue = false;
				NetworkConfig.TransportPacketSender = sender;
				ReadyManager.ResetSessionState();
				PacketSender.ResetSessionState();
				PacketHandler.ResetSessionState();
				MultiplayerSession.ConnectedPlayers.Clear();
				MultiplayerSession.IsHost = true;
				MultiplayerSession.HostUserID = 1;
				MultiplayerSession.InSession = false;
				var client = new MultiplayerPlayer(2);
				client.BeginConnection(hostConnection);
				client.ProtocolVerified = true;
				MultiplayerSession.ConnectedPlayers.Add(2, client);
				if (!ReadyManager.BeginSyncBarrier(2)
				    || !ReadyManager.BeginSnapshotEpoch(2, out long generation)
				    || !ReadyManager.SetPlayerReadyState(client, ClientReadyState.Loading, 99, generation)
				    || !ReadyManager.TryBeginWorldBaseline(2, generation)
				    || ReliableSyncBacklog.TryBuffer(2, new ReplayProbePacket { Value = 1 },
					    PacketSendMode.Reliable) != SyncBacklogResult.Buffered
				    || throwSecond && ReliableSyncBacklog.TryBuffer(
					    2, new ReplayProbePacket { Value = 2 }, PacketSendMode.Reliable)
				    != SyncBacklogResult.Buffered
				    || !ReadyManager.SetPlayerReadyState(client, ClientReadyState.Ready, 99, generation)
				    || !ReadyManager.SetPlayerReadyState(client, ClientReadyState.Ready, 99, generation)
				    || sender.Packets.Count != 1 || client.readyState == ClientReadyState.Ready)
					return UnitTestResult.Fail("Could not arrange one pending replay page");

				var host = new MultiplayerPlayer(1);
				host.BeginConnection(new object());
				MultiplayerSession.ConnectedPlayers.Add(1, host);
				MultiplayerSession.IsHost = false;
				ReplayProbePacket.Applied = 0;
				PacketHandler.BypassReadyGateForTests = true;
				PacketHandler.BypassTrackingForTests = true;
				bool received = PacketHandler.TryHandleIncoming(
					PacketSender.SerializePacketForSending((OrderedReliablePacket)sender.Packets[0]),
					new DispatchContext(1, true, host.ConnectionGeneration));
				if (throwSecond)
				{
					MultiplayerSession.IsHost = true;
					PacketSender.DropConnection(hostConnection);
					return !received && sender.Packets.Count == 1
					       && client.readyState == ClientReadyState.Loading
					       && !ReadyManager.HasPendingReadyCommitForTests(2)
						? UnitTestResult.Pass("Rejected application produced no ACK and aborted pending Ready")
						: UnitTestResult.Fail("Rejected replay leaked an ACK or pending Ready commit");
				}

				if (!received || sender.Packets.Count != 2 || ReplayProbePacket.Applied != 1)
					return UnitTestResult.Fail("Client did not apply replay before returning ACK");
				MultiplayerSession.IsHost = true;
				bool acked = PacketHandler.TryHandleIncoming(
					PacketSender.SerializePacketForSending((OrderedReliablePacket)sender.Packets[1]),
					new DispatchContext(2, false, client.ConnectionGeneration));
				return acked && client.readyState == ClientReadyState.Ready && sender.Packets.Count == 3
					? UnitTestResult.Pass("Receiver application ACK is the sole Ready commit point")
					: UnitTestResult.Fail("Host committed before or failed after the real application ACK");
			}
			finally
			{
				ReadyManager.ResetSessionState();
				PacketHandler.BypassReadyGateForTests = false;
				PacketHandler.BypassTrackingForTests = false;
				PacketSender.ResetSessionState();
				MultiplayerSession.ConnectedPlayers.Clear();
				foreach (var pair in originalPlayers)
					MultiplayerSession.ConnectedPlayers.Add(pair.Key, pair.Value);
				MultiplayerSession.IsHost = originalHost;
				MultiplayerSession.HostUserID = originalHostId;
				MultiplayerSession.InSession = originalSession;
				NetworkConfig.TransportPacketSender = originalSender;
				Configuration.Instance.EnablePacketQueue = originalQueue;
			}
		}
	}
}
#endif
