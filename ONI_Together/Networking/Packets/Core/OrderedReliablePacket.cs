using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;

namespace ONI_Together.Networking.Packets.Core
{
	internal enum OrderedReliableAcceptResult
	{
		Applied,
		Buffered,
		Duplicate,
		Abandoned,
		Overflow,
		Terminated,
	}

	internal sealed class OrderedReliableSendCursor
	{
		private long _committed;

		internal long Reserve() => _committed + 1;

		internal bool Commit(long sequence)
		{
			if (sequence != _committed + 1)
				return false;
			_committed = sequence;
			return true;
		}

		internal long Committed => _committed;
	}

	internal sealed class OrderedReliableReceiveBuffer
	{
		private readonly int _maxPending;
		private readonly int _maxBytes;
		private readonly SortedDictionary<long, byte[]> _pending = new();
		private long _nextSequence = 1;
		private int _pendingBytes;
		private bool _overflowed;

		internal OrderedReliableReceiveBuffer(int maxPending, int maxBytes)
		{
			if (maxPending <= 0 || maxBytes <= 0)
				throw new ArgumentOutOfRangeException();
			_maxPending = maxPending;
			_maxBytes = maxBytes;
		}

		internal OrderedReliableAcceptResult Accept(
			long sequence,
			byte[] payload,
			Action<byte[]> apply,
			Func<bool> canContinue = null)
		{
			if (_overflowed)
				return OrderedReliableAcceptResult.Terminated;
			if (sequence <= 0 || payload == null || payload.Length < sizeof(int))
				return Terminate();
			if (sequence < _nextSequence)
				return OrderedReliableAcceptResult.Duplicate;
			if (sequence == _nextSequence)
			{
				try
				{
					if (!ApplyContiguous(payload, apply, canContinue ?? (() => true)))
						return Abandon();
				}
				catch
				{
					return Terminate();
				}
				return OrderedReliableAcceptResult.Applied;
			}
			return Buffer(sequence, payload);
		}

		private OrderedReliableAcceptResult Buffer(long sequence, byte[] payload)
		{
			if (_pending.TryGetValue(sequence, out byte[] existing))
				return existing.SequenceEqual(payload)
					? OrderedReliableAcceptResult.Duplicate
					: Terminate();
			if (_pending.Count >= _maxPending || payload.Length > _maxBytes - _pendingBytes)
				return Terminate();
			_pending.Add(sequence, payload);
			_pendingBytes += payload.Length;
			return OrderedReliableAcceptResult.Buffered;
		}

		private bool ApplyContiguous(
			byte[] payload, Action<byte[]> apply, Func<bool> canContinue)
		{
			if (!canContinue())
				return false;
			ApplyOne(payload, apply);
			if (!canContinue())
				return false;
			while (_pending.TryGetValue(_nextSequence, out byte[] next))
			{
				_pending.Remove(_nextSequence);
				_pendingBytes -= next.Length;
				ApplyOne(next, apply);
				if (!canContinue())
					return false;
			}
			return true;
		}

		private void ApplyOne(byte[] payload, Action<byte[]> apply)
		{
			apply(payload);
			_nextSequence++;
		}

		internal OrderedReliableAcceptResult Reject() => Terminate();

		private OrderedReliableAcceptResult Abandon()
		{
			_pending.Clear();
			_pendingBytes = 0;
			return OrderedReliableAcceptResult.Abandoned;
		}

		private OrderedReliableAcceptResult Terminate()
		{
			if (_overflowed)
				return OrderedReliableAcceptResult.Terminated;
			_overflowed = true;
			_pending.Clear();
			_pendingBytes = 0;
			return OrderedReliableAcceptResult.Overflow;
		}
	}

	internal sealed class OrderedReliablePacket : IPacket
	{
		internal const int MaxPayloadBytes = 16 * 1024 * 1024 - 64;

		public OrderedReliablePacket() { }

		internal OrderedReliablePacket(long sequence, byte[] payload)
		{
			Sequence = sequence;
			Payload = payload ?? Array.Empty<byte>();
		}

		internal long Sequence { get; private set; }
		internal byte[] Payload { get; private set; } = Array.Empty<byte>();

		public void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(Sequence);
			writer.Write(Payload.Length);
			writer.Write(Payload);
		}

		public void Deserialize(BinaryReader reader)
		{
			Sequence = reader.ReadInt64();
			int length = reader.ReadInt32();
			ValidateLength(length);
			Payload = reader.ReadBytes(length);
			if (Payload.Length != length)
				throw new EndOfStreamException("Ordered reliable payload is truncated");
			Validate();
		}

		public void OnDispatched()
		{
			if (!OrderedReliableChannel.Receive(this, PacketHandler.CurrentContext))
				throw new InvalidDataException("Ordered reliable envelope was rejected");
		}

		private void Validate()
		{
			if (Sequence <= 0)
				throw new InvalidDataException("Invalid ordered reliable sequence");
			ValidateLength(Payload?.Length ?? 0);
		}

		private static void ValidateLength(int length)
		{
			if (length < sizeof(int) || length > MaxPayloadBytes)
				throw new InvalidDataException($"Invalid ordered reliable payload length: {length}");
		}
	}

	internal static class OrderedReliableChannel
	{
		private const int MaxPendingPackets = 4096;
		private const int MaxPendingBytes = 16 * 1024 * 1024;
		private static readonly Dictionary<object, OrderedReliableSendCursor> Outgoing = new();
		private static readonly Dictionary<IncomingKey, OrderedReliableReceiveBuffer> Incoming = new();

		internal static bool ShouldWrap(IPacket packet, PacketSendMode sendMode)
			=> packet is not OrderedReliablePacket && (sendMode & PacketSendMode.Reliable) != 0;

		internal static OrderedReliablePacket PrepareOutgoing(object connection, IPacket packet)
		{
			if (connection == null || packet == null)
				throw new ArgumentNullException();
			if (!Outgoing.TryGetValue(connection, out OrderedReliableSendCursor cursor))
				Outgoing[connection] = cursor = new OrderedReliableSendCursor();
			return new OrderedReliablePacket(
				cursor.Reserve(), PacketSender.SerializePacketForSending(packet));
		}

		internal static bool CommitOutgoing(object connection, long sequence)
			=> connection != null && Outgoing.TryGetValue(connection, out var cursor)
			   && cursor.Commit(sequence);

		internal static long CurrentOutgoingSequence(object connection)
			=> connection != null && Outgoing.TryGetValue(connection, out var cursor)
				? cursor.Committed
				: 0;

		internal static bool Receive(OrderedReliablePacket packet, DispatchContext context)
		{
			var key = new IncomingKey(
				context.SenderId, context.ConnectionGeneration, context.SessionEpoch);
			OrderedReliableReceiveBuffer buffer = GetOrCreateIncoming(context);
			if (!IsPageControlPayload(packet.Payload))
			{
				if (buffer.Reject() == OrderedReliableAcceptResult.Overflow)
					TerminateConnection(context);
				return false;
			}
			OrderedReliableAcceptResult result = buffer.Accept(
				packet.Sequence, packet.Payload,
					payload =>
					{
						if (!PacketHandler.TryHandleIncoming(payload, context.AsOrderedInner())
						    && IsCurrentIncoming(key, buffer, context))
							throw new InvalidDataException("Ordered reliable inner packet was rejected");
					},
					() => IsCurrentIncoming(key, buffer, context));
			if (result == OrderedReliableAcceptResult.Overflow)
				TerminateConnection(context);
			return result is not (OrderedReliableAcceptResult.Overflow
				or OrderedReliableAcceptResult.Terminated);
		}

		internal static bool IsPageControlPayload(byte[] payload)
		{
			if (payload == null || payload.Length < sizeof(int))
				return false;
			int packetId = BitConverter.ToInt32(payload, 0);
			return packetId == API_Helper.GetHashCode(typeof(ReliablePagePacket))
			       || packetId == API_Helper.GetHashCode(typeof(ReliablePageAckPacket));
		}

		internal static bool IsNestedEnvelopePayload(byte[] payload)
			=> payload != null && payload.Length >= sizeof(int)
			   && BitConverter.ToInt32(payload, 0)
			   == API_Helper.GetHashCode(typeof(OrderedReliablePacket));

		internal static void RejectMalformed(DispatchContext context)
		{
			if (GetOrCreateIncoming(context).Reject() == OrderedReliableAcceptResult.Overflow)
				TerminateConnection(context);
		}

		internal static OrderedReliableReceiveBuffer GetOrCreateIncoming(DispatchContext context)
		{
			var key = new IncomingKey(
				context.SenderId, context.ConnectionGeneration, context.SessionEpoch);
			if (Incoming.TryGetValue(key, out OrderedReliableReceiveBuffer buffer))
				return buffer;
			buffer = new OrderedReliableReceiveBuffer(MaxPendingPackets, MaxPendingBytes);
			Incoming.Add(key, buffer);
			return buffer;
		}

		private static bool IsCurrentIncoming(
			IncomingKey key,
			OrderedReliableReceiveBuffer buffer,
			DispatchContext context)
			=> Incoming.TryGetValue(key, out OrderedReliableReceiveBuffer current)
			   && ReferenceEquals(current, buffer)
			   && PacketHandler.IsCurrentDispatchContext(context);

		internal static bool IsCurrentIncomingForTests(
			DispatchContext context, OrderedReliableReceiveBuffer buffer)
		{
			var key = new IncomingKey(
				context.SenderId, context.ConnectionGeneration, context.SessionEpoch);
			return Incoming.TryGetValue(key, out OrderedReliableReceiveBuffer current)
			       && ReferenceEquals(current, buffer);
		}

		internal static void DropConnection(object connection)
		{
			if (connection != null)
				Outgoing.Remove(connection);
		}

		internal static void DropIncoming(ulong senderId)
		{
			foreach (IncomingKey key in Incoming.Keys.Where(key => key.SenderId == senderId).ToList())
				Incoming.Remove(key);
		}

		internal static void ResetSessionState()
		{
			Outgoing.Clear();
			Incoming.Clear();
		}

		private static void TerminateConnection(DispatchContext context)
		{
			DebugConsole.LogError(
				$"[OrderedReliable] Gap buffer overflow from {context.SenderId}; disconnecting.", false);
			PacketSender.TerminateIncomingReliableStream(context);
		}

		private readonly struct IncomingKey : IEquatable<IncomingKey>
		{
			internal IncomingKey(ulong senderId, long connectionGeneration, long sessionEpoch)
			{
				SenderId = senderId;
				ConnectionGeneration = connectionGeneration;
				SessionEpoch = sessionEpoch;
			}

			internal ulong SenderId { get; }
			private long ConnectionGeneration { get; }
			private long SessionEpoch { get; }

			public bool Equals(IncomingKey other)
				=> SenderId == other.SenderId
				   && ConnectionGeneration == other.ConnectionGeneration
				   && SessionEpoch == other.SessionEpoch;

			public override bool Equals(object obj)
				=> obj is IncomingKey other && Equals(other);

			public override int GetHashCode()
			{
				int hash = SenderId.GetHashCode();
				hash = hash * 397 ^ ConnectionGeneration.GetHashCode();
				return hash * 397 ^ SessionEpoch.GetHashCode();
			}
		}
	}
}
