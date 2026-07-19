using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Shared.Interfaces.Networking;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Core
{
	internal interface IHostAuthoritativeRelay
	{
	}

	/// <summary>
	/// used by clients to broadcast a packet to all other clients via the host
	/// </summary>
	internal class HostBroadcastPacket : IPacket
	{
		internal enum SequenceLane : byte
		{
			Ordered,
			CursorSnapshot,
		}

		public const int MaxInnerPacketBytes = 1024 * 1024;
		private const int RelayWireOverheadBytes =
			sizeof(int) + sizeof(int) + sizeof(ulong) + sizeof(ulong) + sizeof(int);
		private const int MaxCompletedRequests = 2048;
		private const int MaxTrackedSenders = 256;
		private static readonly object RequestHistoryLock = new();
		private static readonly Dictionary<
			(ulong Sender, long Generation, SequenceLane Lane), ulong> LatestRequests = new();
		private static readonly Queue<(
			(ulong Sender, long Generation, SequenceLane Lane) Key,
			ulong RequestId)> RequestOrder = new();
		private static readonly HashSet<(
			(ulong Sender, long Generation, SequenceLane Lane) Key,
			ulong RequestId)> CompletedRequests = new();
		private static long _nextRequestId;

		public static void ResetSessionState()
		{
			lock (RequestHistoryLock)
			{
				LatestRequests.Clear();
				RequestOrder.Clear();
				CompletedRequests.Clear();
				Interlocked.Exchange(ref _nextRequestId, 0);
			}
		}

		public HostBroadcastPacket() { }
		public HostBroadcastPacket(IPacket innerPacket, ulong sender)
		{
			using var _ = Profiler.Scope();

			InnerPacketId = API_Helper.GetHashCode(innerPacket.GetType());
			using var ms = new MemoryStream();
			using var writer = new BinaryWriter(ms);
			innerPacket.Serialize(writer);
			InnerPacketData = ms.ToArray();
			SenderId = sender;
			RequestId = unchecked((ulong)Interlocked.Increment(ref _nextRequestId));
		}

		internal static PacketSendMode GetRelaySendMode(IPacket packet)
			=> packet is PlayerCursorPacket
				? PacketSendMode.Unreliable
				: PacketSendMode.Reliable;

		internal static int GetRelayWireSize(IPacket innerPacket)
		{
			if (innerPacket == null)
				return int.MaxValue;
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(
				       stream, System.Text.Encoding.UTF8, leaveOpen: true))
				innerPacket.Serialize(writer);
			return checked(RelayWireOverheadBytes + (int)stream.Length);
		}

		internal static bool TryFitUnreliableRelay(PlayerCursorPacket cursor)
		{
			if (cursor == null)
				return false;
			if (GetRelayWireSize(cursor) <= PacketSender.MAX_PACKET_SIZE_UNRELIABLE)
				return true;
			cursor.HasUtilityPath = false;
			cursor.UtilityPathData = null;
			return GetRelayWireSize(cursor) <= PacketSender.MAX_PACKET_SIZE_UNRELIABLE;
		}


		int InnerPacketId;
		public ulong SenderId;
		public ulong RequestId;
		byte[] InnerPacketData = Array.Empty<byte>();

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(InnerPacketId);
			writer.Write(SenderId);
			writer.Write(RequestId);
			writer.Write(InnerPacketData.Length);
			writer.Write(InnerPacketData);
		}
		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			InnerPacketId = reader.ReadInt32();
			SenderId = reader.ReadUInt64();
			RequestId = reader.ReadUInt64();
			int dataLength = reader.ReadInt32();
			if (dataLength < 0 || dataLength > MaxInnerPacketBytes)
				throw new InvalidDataException($"Invalid host-broadcast payload length: {dataLength}");
			if (reader.BaseStream.CanSeek && reader.BaseStream.Length - reader.BaseStream.Position < dataLength)
				throw new EndOfStreamException("Host-broadcast payload is truncated");
			InnerPacketData = reader.ReadBytes(dataLength);
			if (InnerPacketData.Length != dataLength)
				throw new EndOfStreamException("Host-broadcast payload is truncated");
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			DispatchContext context = PacketHandler.CurrentContext;
			if (!MultiplayerSession.IsHost)
			{
				DebugConsole.LogWarning("[HostBroadcastPacket] clients cannot receive relay wrappers");
				return;
			}
			if (context.SenderIsHost || context.SenderId != SenderId)
			{
				DebugConsole.LogWarning($"[HostBroadcastPacket] sender mismatch: transport={context.SenderId}, wire={SenderId}");
				return;
			}
			if (MultiplayerSession.GetPlayer(context.SenderId)?.ProtocolVerified != true)
			{
				DebugConsole.LogWarning($"[HostBroadcastPacket] rejected unverified sender {context.SenderId}");
				return;
			}
			SequenceLane lane = GetSequenceLane(InnerPacketId);
			if (!IsRequestFresh(
				    context.SenderId, RequestId, context.ConnectionGeneration, lane))
			{
				DebugConsole.LogWarning($"[HostBroadcastPacket] rejected stale request {RequestId} from {context.SenderId}");
				return;
			}
			if (!PacketRegistry.HasRegisteredPacket(InnerPacketId))
			{
				DebugConsole.LogWarning("[HostBroadcastPacket] unknown inner packet id found, cannot rebroadcast: "+InnerPacketId);
				return;
			}
			var innerPacket = PacketRegistry.Create(InnerPacketId);
			if (innerPacket is not IClientRelayable
			    && !PacketRegistry.CanClientDispatchModApi(innerPacket, relayed: true))
			{
				DebugConsole.LogWarning($"[HostBroadcastPacket] {innerPacket.GetType().Name} is not client-relayable");
				return;
			}
			using var ms = new MemoryStream(InnerPacketData);
			using var reader = new BinaryReader(ms);
			innerPacket.Deserialize(reader);
			if (reader.BaseStream.Position != reader.BaseStream.Length)
			{
				DebugConsole.LogWarning($"[HostBroadcastPacket] trailing inner payload for {innerPacket.GetType().Name}");
				return;
			}
			if (!IsInnerSenderValid(innerPacket, context.SenderId))
			{
				DebugConsole.LogWarning($"[HostBroadcastPacket] payload sender mismatch for {innerPacket.GetType().Name}: transport={context.SenderId}");
				return;
			}
			DebugConsole.Log("[HostBroadcastPacket] received packet of type " + innerPacket.GetType().Name+", dispatching");
			if (DispatchVerifiedRelayAndFanOut(
				innerPacket,
				context,
				PacketHandler.DispatchNested,
				static (packet, senderId) => PacketSender.SendToAllExcluding(
					packet, [MultiplayerSession.HostUserID, senderId],
					GetRelaySendMode(packet))))
			{
				TryBeginRequest(
					context.SenderId, RequestId, context.ConnectionGeneration, lane);
			}
		}

		internal static bool IsInnerSenderValid(IPacket innerPacket, ulong transportSenderId)
			=> innerPacket is not ISenderBoundRelay senderBound || senderBound.RelaySenderId == transportSenderId;

		internal static bool TryBeginRequest(
			ulong senderId, ulong requestId, long generation = 0,
			SequenceLane lane = SequenceLane.Ordered)
		{
			if (senderId == 0 || requestId == 0)
				return false;
			lock (RequestHistoryLock)
			{
				var key = (senderId, generation, lane);
				var completed = (key, requestId);
				if (CompletedRequests.Contains(completed)
				    || LatestRequests.TryGetValue(key, out ulong latest) && requestId <= latest)
					return false;
				LatestRequests[key] = requestId;
				CompletedRequests.Add(completed);
				RequestOrder.Enqueue((key, requestId));
				TrimRequestHistory();
				return true;
			}
		}

		private static bool IsRequestFresh(
			ulong senderId, ulong requestId, long generation, SequenceLane lane)
		{
			if (senderId == 0 || requestId == 0)
				return false;
			lock (RequestHistoryLock)
			{
				var key = (senderId, generation, lane);
				return !CompletedRequests.Contains((key, requestId))
				    && (!LatestRequests.TryGetValue(key, out ulong latest) || requestId > latest);
			}
		}

		private static SequenceLane GetSequenceLane(int innerPacketId)
			=> innerPacketId == API_Helper.GetHashCode(typeof(PlayerCursorPacket))
				? SequenceLane.CursorSnapshot
				: SequenceLane.Ordered;

		private static void TrimRequestHistory()
		{
			while (RequestOrder.Count > MaxCompletedRequests
			       || LatestRequests.Count > MaxTrackedSenders)
			{
				var oldest = RequestOrder.Dequeue();
				CompletedRequests.Remove(oldest);
				if (LatestRequests.TryGetValue(oldest.Key, out ulong latest)
				    && latest == oldest.RequestId)
					LatestRequests.Remove(oldest.Key);
			}
		}

		internal static bool DispatchVerifiedRelayAndFanOut(
			IPacket innerPacket,
			DispatchContext transportContext,
			Func<IPacket, DispatchContext, bool> dispatch,
			Action<IPacket, ulong> fanOut)
		{
			if (!dispatch(innerPacket, transportContext.AsVerifiedHostBroadcast()))
				return false;

			if (innerPacket is not IHostAuthoritativeRelay)
				fanOut(innerPacket, transportContext.SenderId);
			return true;
		}

	}
}
