using Epic.OnlineServices.P2P;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Transport;
using ONI_Together.Networking.Transport.Steam;
using Shared.Interfaces.Networking;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Shared.Profiling;
using UnityEngine;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.States;

namespace ONI_Together.Networking
{
	public static partial class PacketSender
	{
		internal static bool CanSendPeerRuntime(bool isHost, ClientState clientState)
			=> isHost || GameClient.CanSendRuntimeRequests(clientState);

		private class PacketUpdateRunner
		{
			private readonly float _updateIntervalS;
			private readonly Dictionary<object, float> _lastDispatchTime = [];

			public PacketUpdateRunner(int packetId, uint updateInterval)
			{
				_updateIntervalS = updateInterval / 1000f;
			}

			public bool CanDispatchNext(object connection)
			{
				using var _ = Profiler.Scope();

				if (!_lastDispatchTime.TryGetValue(connection, out var lastDispatchTime))
					return true;

				return Time.unscaledTime - lastDispatchTime >= _updateIntervalS;
			}

			public void RecordDispatch(object connection)
			{
				using var _ = Profiler.Scope();

				_lastDispatchTime[connection] = Time.unscaledTime;
			}

			public void DropConnection(object connection) => _lastDispatchTime.Remove(connection);
		}

		// Kilobytes
        public static float MAX_PACKET_SIZE_LAN = 0.5f; // 512 bytes (is multipled by 1024)
        public static int MAX_PACKET_SIZE_RELIABLE = 512;
		public const int MAX_PACKET_SIZE_UNRELIABLE = 1000;

        public static byte[] SerializePacketForSending(IPacket packet)
		{
			using var _ = Profiler.Scope();

			using (var ms = new System.IO.MemoryStream())
			using (var writer = new System.IO.BinaryWriter(ms))
			{
				int packet_type = PacketRegistry.GetPacketId(packet);
				writer.Write(packet_type);
				packet.Serialize(writer);
				return ms.ToArray();
			}
		}

		static Dictionary<int, PacketUpdateRunner> UpdateRunners = [];
		static Dictionary<object, Dictionary<int, List<byte[]>>> WaitingBulkPacketsPerReceiver = [];
		private static readonly object SendGate = new();
		private static readonly HashSet<(ulong Sender, long Generation, long Epoch)>
			TerminatedIncomingStreams = new();
		private static readonly ReliablePageChannel PageChannel = new(
			SendReliablePageLocked,
			SendReliablePageAck,
			PacketHandler.TryHandleIncomingReliableFrame,
			TerminateOutgoingPageStream,
			TerminateIncomingReliableStream,
			isContextCurrent: PacketHandler.IsCurrentDispatchContext);
#if DEBUG
		internal static Action<object> OutgoingPageTerminationForTests;
		internal static Action<DispatchContext> IncomingPageTerminationForTests;
#endif
		// Running byte total per (receiver, packetId) so LAN capacity checks stay O(1) per append.
		static Dictionary<object, Dictionary<int, int>> WaitingBulkPacketBytes = [];
		// Packet ids that belong to DragToolPacket subclasses — tagged lazily on first append
		// so the bulk flush site can record SyncStats.DragTool without needing the typed instance.
		static HashSet<int> DragToolBulkPacketIds = new HashSet<int>();

		public static void ResetSessionState()
		{
			lock (SendGate)
			{
				UpdateRunners.Clear();
				WaitingBulkPacketsPerReceiver.Clear();
				WaitingBulkPacketBytes.Clear();
				DragToolBulkPacketIds.Clear();
				_sendErrorCount = 0;
				TerminatedIncomingStreams.Clear();
				PageChannel.Reset();
				OrderedReliableChannel.ResetSessionState();
			}
		}

		public static void DropConnection(object connection)
		{
			if (connection == null)
				return;
			lock (SendGate)
			{
				WaitingBulkPacketsPerReceiver.Remove(connection);
				WaitingBulkPacketBytes.Remove(connection);
				foreach (PacketUpdateRunner runner in UpdateRunners.Values)
					runner.DropConnection(connection);
				PageChannel.DropConnection(connection);
				OrderedReliableChannel.DropConnection(connection);
			}
		}

		internal static void DropIncoming(ulong senderId)
		{
			lock (SendGate)
			{
				PageChannel.DropIncoming(senderId);
				TerminatedIncomingStreams.RemoveWhere(key => key.Sender == senderId);
			}
		}

		internal static int PendingBulkCountForTests(object connection)
		{
			lock (SendGate)
			{
				if (connection == null
				    || !WaitingBulkPacketsPerReceiver.TryGetValue(connection, out var packetsByType))
					return 0;
				return packetsByType.Values.Sum(packets => packets.Count);
			}
		}

		internal static bool HasPendingReliable(object connection)
		{
			lock (SendGate)
				return PageChannel.HasPendingReliable(connection);
		}

		public static void DispatchPendingBulkPackets()
		{
			lock (SendGate)
				DispatchPendingBulkPacketsLocked();
		}

		private static void DispatchPendingBulkPacketsLocked()
		{
			using var _ = Profiler.Scope();
			PageChannel.ExpireStalledTransfers();

			var emptyConnections = new List<object>();
			foreach (var kvp in WaitingBulkPacketsPerReceiver)
			{
				var conn = kvp.Key;
				foreach (var packetId in kvp.Value.Keys.ToList())
				{
					DispatchPendingBulkPacketOfType(conn, packetId, true);
				}

				if (kvp.Value.Count == 0)
					emptyConnections.Add(conn);
			}

			foreach (var conn in emptyConnections)
			{
				WaitingBulkPacketsPerReceiver.Remove(conn);
				WaitingBulkPacketBytes.Remove(conn);
			}
		}

		static bool DispatchPendingBulkPacketOfType(object conn, int packetId, bool intervalRun = false)
		{
			using var _ = Profiler.Scope();

			if (!WaitingBulkPacketsPerReceiver.TryGetValue(conn, out var allPendingPackets)
				|| !allPendingPackets.TryGetValue(packetId, out var pendingPackets)
				|| !pendingPackets.Any())
			{
				return true;
			}
			if (intervalRun && UpdateRunners.TryGetValue(packetId, out var intervalRunner) && !intervalRunner.CanDispatchNext(conn))
				return true;

			int flushCount = pendingPackets.Count;
			int flushBytes = 0;
			WaitingBulkPacketBytes.TryGetValue(conn, out var byteTotals);
			if (byteTotals != null && byteTotals.TryGetValue(packetId, out var bt))
				flushBytes = bt;
			var swFlush = System.Diagnostics.Stopwatch.StartNew();
			bool sent = SendToConnection(
				conn,
				new BulkSenderPacket(packetId, pendingPackets),
				PacketSendMode.ReliableImmediate);
			swFlush.Stop();
			if (!sent)
			{
				DebugConsole.LogWarning(
					$"[PacketSender] Retaining {flushCount} failed bulk packet(s) of type {packetId} for retry");
				return false;
			}
			pendingPackets.Clear();
			allPendingPackets.Remove(packetId);
			if (byteTotals != null)
			{
				byteTotals[packetId] = 0;
				byteTotals.Remove(packetId);
			}
			if (UpdateRunners.TryGetValue(packetId, out var runner))
				runner.RecordDispatch(conn);
			if (DragToolBulkPacketIds.Contains(packetId))
				SyncStats.RecordSync(SyncStats.DragTool, flushCount, flushBytes, (float)swFlush.Elapsed.TotalMilliseconds);
			return true;
		}

		private static bool FlushPendingBulkBeforeReliable(object conn)
		{
			if (conn == null
			    || !WaitingBulkPacketsPerReceiver.TryGetValue(conn, out var pendingByType))
				return true;

			foreach (int packetId in pendingByType.Keys.ToList())
			{
				if (!DispatchPendingBulkPacketOfType(conn, packetId))
					return false;
			}
			return true;
		}
		public static bool AppendPendingBulkPacket(object conn, IPacket packet, IBulkablePacket bp)
		{
			lock (SendGate)
				return AppendPendingBulkPacketLocked(conn, packet, bp);
		}

		private static bool AppendPendingBulkPacketLocked(
			object conn, IPacket packet, IBulkablePacket bp)
		{
			using var _ = Profiler.Scope();

			int packetId = PacketRegistry.GetPacketId(packet);
			int maxPacketNumberPerPacket = bp.MaxPackSize;
			byte[] serialized = packet.SerializeToByteArray();

			if (packet is ONI_Together.Networking.Packets.Tools.DragToolPacket)
				DragToolBulkPacketIds.Add(packetId);

			if (!UpdateRunners.ContainsKey(packetId))
			{
				UpdateRunners[packetId] = new PacketUpdateRunner(packetId, bp.IntervalMs);
			}

			if (!WaitingBulkPacketsPerReceiver.TryGetValue(conn, out var bulkPacketWaitingData))
			{
				WaitingBulkPacketsPerReceiver[conn] = [];
				bulkPacketWaitingData = WaitingBulkPacketsPerReceiver[conn];
			}
			foreach (int pendingPacketId in bulkPacketWaitingData.Keys.ToList())
			{
				if (pendingPacketId != packetId
				    && !DispatchPendingBulkPacketOfType(conn, pendingPacketId))
					return false;
			}
			if (!bulkPacketWaitingData.TryGetValue(packetId, out var pendingPackets))
			{
				bulkPacketWaitingData[packetId] = new List<byte[]>(maxPacketNumberPerPacket);
				pendingPackets = bulkPacketWaitingData[packetId];
			}
			pendingPackets.Add(serialized);

			if (!WaitingBulkPacketBytes.TryGetValue(conn, out var byteTotals))
			{
				byteTotals = [];
				WaitingBulkPacketBytes[conn] = byteTotals;
			}
			if (!byteTotals.TryGetValue(packetId, out var runningTotal))
				runningTotal = 4; // +4 for the packetId int header
			runningTotal += serialized.Length;
			byteTotals[packetId] = runningTotal;

			bool atCapacity = false;
			if (NetworkConfig.IsLanConfig())
			{
				float maxSize = MAX_PACKET_SIZE_LAN * 1024f;
				if (runningTotal >= maxSize)
				{
					atCapacity = true;
				}
			}

			if (pendingPackets.Count >= maxPacketNumberPerPacket || atCapacity)
			{
				return DispatchPendingBulkPacketOfType(conn, packetId);
			}
			return true;
		}
		public static byte[] SerializeToByteArray(this IPacket packet)
		{
			using var _ = Profiler.Scope();

			using var ms = new System.IO.MemoryStream();
			using var writer = new System.IO.BinaryWriter(ms);
			packet.Serialize(writer);
			return ms.ToArray();
		}

		/// <summary>
		/// Send to one connection by HSteamNetConnection handle.
		/// </summary>
		///

		public static bool SendToConnection(object conn, IPacket packet, PacketSendMode sendType = PacketSendMode.ReliableImmediate)
		{
			using var _ = Profiler.Scope();
			if (conn == null || packet == null)
				return false;
			lock (SendGate)
				return SendToConnectionLocked(conn, packet, sendType, null);
		}

		internal static bool SendReliableWithCompletion(
			object conn, IPacket packet, Action<bool> completion)
		{
			if (conn == null || packet == null || completion == null)
				return false;
			lock (SendGate)
				return SendToConnectionLocked(
					conn, packet, PacketSendMode.ReliableImmediate, completion);
		}

		private static bool SendToConnectionLocked(
			object conn, IPacket packet, PacketSendMode sendType, Action<bool> completion)
		{
			try
			{
				if (packet is IBulkablePacket bp)
					return AppendPendingBulkPacket(conn, packet, bp);
				if ((sendType & PacketSendMode.Reliable) != 0
				    && packet is not BulkSenderPacket
				    && !FlushPendingBulkBeforeReliable(conn))
					return false;

				byte[] payload = SerializePacketForSending(packet);
				if ((sendType & PacketSendMode.Reliable) == 0)
					return payload.Length <= MAX_PACKET_SIZE_UNRELIABLE
					       && NetworkConfig.TransportPacketSender != null
					       && NetworkConfig.TransportPacketSender.SendToConnection(conn, packet, sendType);
				if (packet is IReliablePageControl)
					return SendOrderedControlLocked(conn, packet);
				if (packet is OrderedReliablePacket)
					return false;
				return PageChannel.TryEnqueue(conn, payload, completion);
			}
			catch (Exception ex)
			{
				LogSendFailure(packet, ex);
				return false;
			}
		}

		private static bool SendReliablePageLocked(object connection, ReliablePagePacket page)
			=> SendOrderedControlLocked(connection, page);

		private static bool SendReliablePageAck(
			DispatchContext context, ReliablePageAckPacket ack)
			=> SendToPlayer(context.SenderId, ack, PacketSendMode.ReliableImmediate);

		private static bool SendOrderedControlLocked(object connection, IPacket control)
		{
			if (NetworkConfig.TransportPacketSender == null)
				return false;
			OrderedReliablePacket ordered = OrderedReliableChannel.PrepareOutgoing(connection, control);
			return NetworkConfig.TransportPacketSender.SendToConnection(
				       connection, ordered, PacketSendMode.ReliableImmediate)
			       && OrderedReliableChannel.CommitOutgoing(connection, ordered.Sequence);
		}

		internal static bool AcceptReliablePage(
			ReliablePagePacket page, DispatchContext context)
			=> PageChannel.AcceptPage(page, context);

		internal static bool AcceptReliablePageAck(
			ReliablePageAckPacket ack, DispatchContext context)
		{
			MultiplayerPlayer player = MultiplayerSession.GetPlayer(context.SenderId);
			if (player?.Connection == null)
			{
				TerminateIncomingReliableStream(context);
				return false;
			}
			lock (SendGate)
				return PageChannel.AcceptAck(player.Connection, ack);
		}

		internal static bool AcceptReliablePageAckForTests(
			object connection, ReliablePageAckPacket ack)
		{
			lock (SendGate)
				return PageChannel.AcceptAck(connection, ack);
		}

		private static void TerminateOutgoingPageStream(object connection)
		{
#if DEBUG
			if (OutgoingPageTerminationForTests != null)
			{
				OutgoingPageTerminationForTests(connection);
				return;
			}
#endif
			MultiplayerPlayer player = MultiplayerSession.ConnectedPlayers.Values
				.FirstOrDefault(candidate => Equals(candidate.Connection, connection));
			if (MultiplayerSession.IsHost && player != null)
				NetworkConfig.TransportServer?.KickClient(player.PlayerId);
			else if (!MultiplayerSession.IsHost)
				NetworkConfig.TransportClient?.Disconnect();
		}

		internal static void TerminateIncomingReliableStream(DispatchContext context)
		{
			lock (SendGate)
			{
				var key = (context.SenderId, context.ConnectionGeneration, context.SessionEpoch);
				if (!TerminatedIncomingStreams.Add(key))
					return;
			}
#if DEBUG
			if (IncomingPageTerminationForTests != null)
			{
				IncomingPageTerminationForTests(context);
				return;
			}
#endif
			if (MultiplayerSession.IsHost)
				NetworkConfig.TransportServer?.KickClient(context.SenderId);
			else
				NetworkConfig.TransportClient?.Disconnect();
		}
	}
}
