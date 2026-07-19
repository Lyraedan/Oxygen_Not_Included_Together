using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ONI_Together.Networking.Packets;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;

namespace ONI_Together.Networking
{
	internal sealed partial class ReliablePageChannel
	{
		internal const int MaxOrderedWireBytes = 6 * 980;
		internal const int MaxPageDataBytes = MaxOrderedWireBytes - 44;
		internal const int MaxInFlightPages = 2;
		internal const int MaxQueuedFrames = 4096;
		internal const int MaxQueuedBytes = 16 * 1024 * 1024;
		internal static readonly TimeSpan AckTimeout = TimeSpan.FromSeconds(30);

		private sealed class Frame
		{
			internal byte[] Payload;
			internal Action<bool> Completion;
			internal int Cost => Payload.Length + sizeof(int) * 2;
		}

		private sealed class Transfer
		{
			internal long Id;
			internal List<Frame> Frames;
			internal byte[] Batch;
			internal int PageCount;
			internal int NextPageToSend;
			internal int NextPageToAck;
		}

		private sealed class OutgoingState
		{
			internal readonly Queue<Frame> Pending = new();
			internal readonly LinkedList<Transfer> Transfers = new();
			internal long NextTransferId;
			internal long LastCompletedTransferId;
			internal int InFlightPages;
			internal int QueuedFrames;
			internal int QueuedBytes;
			internal bool Terminated;
			internal System.DateTime LastProgressUtc;
		}

		private sealed class IncomingTransfer
		{
			internal long Id;
			internal int TotalPages;
			internal int TotalBytes;
			internal int NextPage;
			internal readonly MemoryStream Bytes = new();
		}

		private sealed class IncomingState
		{
			internal long NextTransferId = 1;
			internal IncomingTransfer Active;
			internal ReliablePageAckPacket LastCompletedAck;
			internal bool Terminated;
		}

		private readonly Func<object, ReliablePagePacket, bool> _sendPage;
		private readonly Func<DispatchContext, ReliablePageAckPacket, bool> _sendAck;
		private readonly Func<byte[], DispatchContext, bool> _dispatch;
		private readonly Action<object> _terminateOutgoing;
		private readonly Action<DispatchContext> _terminateIncoming;
		private readonly Func<System.DateTime> _utcNow;
		private readonly Func<DispatchContext, bool> _isContextCurrent;
		private readonly Dictionary<object, OutgoingState> _outgoing = new();
		private readonly Dictionary<IncomingKey, IncomingState> _incoming = new();
		private readonly object _incomingGate = new();

		internal ReliablePageChannel(
			Func<object, ReliablePagePacket, bool> sendPage,
			Func<DispatchContext, ReliablePageAckPacket, bool> sendAck,
			Func<byte[], DispatchContext, bool> dispatch,
			Action<object> terminateOutgoing,
			Action<DispatchContext> terminateIncoming,
			Func<System.DateTime> utcNow = null,
			Func<DispatchContext, bool> isContextCurrent = null)
		{
			_sendPage = sendPage ?? throw new ArgumentNullException(nameof(sendPage));
			_sendAck = sendAck ?? throw new ArgumentNullException(nameof(sendAck));
			_dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
			_terminateOutgoing = terminateOutgoing ?? (_ => { });
			_terminateIncoming = terminateIncoming ?? (_ => { });
			_utcNow = utcNow ?? (() => System.DateTime.UtcNow);
			_isContextCurrent = isContextCurrent ?? (_ => true);
		}

		internal static ReliablePageChannel CreateForTests(
			Func<object, ReliablePagePacket, bool> sendPage,
			Func<byte[], DispatchContext, bool> dispatch = null)
			=> new ReliablePageChannel(
				sendPage, (_, __) => true, dispatch ?? ((_, __) => true), _ => { }, _ => { });

		internal bool TryEnqueue(object connection, byte[] payload, Action<bool> completion = null)
		{
			if (connection == null || payload == null || payload.Length < sizeof(int))
				return false;
			if (!_outgoing.TryGetValue(connection, out OutgoingState state))
				_outgoing.Add(connection, state = new OutgoingState { LastProgressUtc = _utcNow() });
			if (state.Terminated)
			{
				completion?.Invoke(false);
				return false;
			}
			int cost = checked(payload.Length + sizeof(int) * 2);
			if (state.QueuedFrames >= MaxQueuedFrames || cost > MaxQueuedBytes - state.QueuedBytes)
			{
				completion?.Invoke(false);
				return TerminateOutgoing(connection, state);
			}

			state.Pending.Enqueue(new Frame { Payload = payload, Completion = completion });
			state.QueuedFrames++;
			state.QueuedBytes += cost;
			return Pump(connection, state);
		}

		internal bool AcceptAck(object connection, ReliablePageAckPacket ack)
		{
			if (connection == null || ack == null
			    || !_outgoing.TryGetValue(connection, out OutgoingState state)
			    || state.Terminated)
				return false;
			if (ack.TransferId <= state.LastCompletedTransferId)
				return true;
			Transfer transfer = state.Transfers.FirstOrDefault(item => item.Id == ack.TransferId);
			if (transfer == null || ack.TransferId > state.NextTransferId
			    || ack.TotalPages != transfer.PageCount || ack.TotalBytes != transfer.Batch.Length)
				return TerminateOutgoing(connection, state);
			if (ack.PageIndex < transfer.NextPageToAck)
				return true;
			if (ack.PageIndex > transfer.NextPageToAck
			    || ack.PageIndex >= transfer.NextPageToSend)
				return TerminateOutgoing(connection, state);

			transfer.NextPageToAck++;
			state.LastProgressUtc = _utcNow();
			state.InFlightPages--;
			if (transfer.NextPageToAck == transfer.PageCount)
			{
				if (!ReferenceEquals(state.Transfers.First?.Value, transfer))
					return TerminateOutgoing(connection, state);
				state.Transfers.RemoveFirst();
				state.LastCompletedTransferId = transfer.Id;
				foreach (Frame frame in transfer.Frames)
				{
					state.QueuedFrames--;
					state.QueuedBytes -= frame.Cost;
					frame.Completion?.Invoke(true);
				}
			}
			return Pump(connection, state);
		}

		internal bool AcceptPage(ReliablePagePacket page, DispatchContext context)
		{
			ReliablePageAckPacket ack = null;
			byte[] completed = null;
			var key = new IncomingKey(context);
			lock (_incomingGate)
			{
				if (!_incoming.TryGetValue(key, out IncomingState state))
					_incoming.Add(key, state = new IncomingState());
				if (state.Terminated)
					return false;
				if (page.TransferId < state.NextTransferId)
				{
					ack = state.LastCompletedAck?.TransferId == page.TransferId
						? state.LastCompletedAck : null;
				}
				else if (page.TransferId > state.NextTransferId)
				{
					state.Terminated = true;
				}
				else if (!TryAcceptContiguousPage(state, page, out ack, out completed))
				{
					state.Terminated = true;
				}
				if (state.Terminated)
				{
					state.Active?.Bytes.Dispose();
					state.Active = null;
				}
			}

			if (IsIncomingTerminated(key))
				return TerminateIncoming(context);
			if (completed != null && !DispatchBatch(completed, context.AsReliablePageInner()))
				return TerminateIncomingState(key, context);
			if (!_isContextCurrent(context))
			{
				AbandonIncoming(key);
				return true;
			}
			if (completed != null)
				CompleteIncoming(key, ack);
			if (ack != null && !_sendAck(context, ack))
				return TerminateIncomingState(key, context);
			return true;
		}

		private bool Pump(object connection, OutgoingState state)
		{
			while (state.InFlightPages < MaxInFlightPages)
			{
				Transfer transfer = state.Transfers.FirstOrDefault(
					item => item.NextPageToSend < item.PageCount);
				if (transfer == null)
				{
					if (state.Pending.Count == 0)
						return true;
					transfer = BuildTransfer(state);
					state.Transfers.AddLast(transfer);
				}

				int pageIndex = transfer.NextPageToSend;
				int offset = pageIndex * MaxPageDataBytes;
				int length = PageLength(transfer.Batch.Length, pageIndex);
				var data = new byte[length];
				Buffer.BlockCopy(transfer.Batch, offset, data, 0, length);
				var page = new ReliablePagePacket(
					transfer.Id, pageIndex, transfer.PageCount, transfer.Batch.Length, data);
				if (!_sendPage(connection, page))
					return TerminateOutgoing(connection, state);
				transfer.NextPageToSend++;
				state.InFlightPages++;
				state.LastProgressUtc = _utcNow();
			}
			return true;
		}

		private static Transfer BuildTransfer(OutgoingState state)
		{
			var frames = new List<Frame>();
			int bytes = sizeof(int);
			do
			{
				Frame next = state.Pending.Peek();
				int nextBytes = sizeof(int) + next.Payload.Length;
				if (frames.Count > 0 && bytes + nextBytes > MaxPageDataBytes)
					break;
				frames.Add(state.Pending.Dequeue());
				bytes += nextBytes;
			}
			while (state.Pending.Count > 0 && bytes < MaxPageDataBytes);

			using var stream = new MemoryStream(bytes);
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
			{
				writer.Write(frames.Count);
				foreach (Frame frame in frames)
				{
					writer.Write(frame.Payload.Length);
					writer.Write(frame.Payload);
				}
			}
			byte[] batch = stream.ToArray();
			return new Transfer
			{
				Id = ++state.NextTransferId,
				Frames = frames,
				Batch = batch,
				PageCount = PageCount(batch.Length)
			};
		}

		private static bool TryAcceptContiguousPage(
			IncomingState state,
			ReliablePagePacket page,
			out ReliablePageAckPacket ack,
			out byte[] completed)
		{
			ack = null;
			completed = null;
			IncomingTransfer active = state.Active;
			if (active == null)
			{
				if (page.PageIndex != 0)
					return false;
				state.Active = active = new IncomingTransfer
				{
					Id = page.TransferId,
					TotalPages = page.TotalPages,
					TotalBytes = page.TotalBytes
				};
			}
			if (active.Id != page.TransferId || active.TotalPages != page.TotalPages
			    || active.TotalBytes != page.TotalBytes || page.PageIndex != active.NextPage)
				return false;
			active.Bytes.Write(page.Data, 0, page.Data.Length);
			active.NextPage++;
			ack = new ReliablePageAckPacket(
				page.TransferId, page.PageIndex, page.TotalPages, page.TotalBytes);
			if (active.NextPage != active.TotalPages)
				return true;
			if (active.Bytes.Length != active.TotalBytes)
				return false;
			completed = active.Bytes.ToArray();
			return true;
		}

		private bool DispatchBatch(byte[] batch, DispatchContext context)
		{
			if (!TryDecodeBatch(batch, out List<byte[]> frames))
				return false;
			if (frames.Any(IsTransportEnvelopeData))
				return false;
			foreach (byte[] frame in frames)
			{
				if (!_isContextCurrent(context))
					return true;
				if (!_dispatch(frame, context) && _isContextCurrent(context))
					return false;
			}
			return true;
		}

		private void AbandonIncoming(IncomingKey key)
		{
			lock (_incomingGate)
			{
				if (!_incoming.TryGetValue(key, out IncomingState state))
					return;
				state.Active?.Bytes.Dispose();
				_incoming.Remove(key);
			}
		}

		private void CompleteIncoming(IncomingKey key, ReliablePageAckPacket ack)
		{
			lock (_incomingGate)
			{
				if (!_incoming.TryGetValue(key, out IncomingState state) || state.Terminated
				    || state.Active?.Id != ack.TransferId)
					return;
				state.Active.Bytes.Dispose();
				state.Active = null;
				state.NextTransferId++;
				state.LastCompletedAck = ack;
			}
		}

		private bool TerminateOutgoing(object connection, OutgoingState state)
			=> FailOutgoing(connection, state, terminateConnection: true);

		private bool FailOutgoing(
			object connection, OutgoingState state, bool terminateConnection)
		{
			if (!state.Terminated)
			{
				state.Terminated = true;
				foreach (Frame frame in state.Pending)
					frame.Completion?.Invoke(false);
				foreach (Transfer transfer in state.Transfers)
					foreach (Frame frame in transfer.Frames)
						frame.Completion?.Invoke(false);
				state.Pending.Clear();
				state.Transfers.Clear();
				state.QueuedBytes = 0;
				state.QueuedFrames = 0;
				state.InFlightPages = 0;
				if (terminateConnection)
					_terminateOutgoing(connection);
			}
			return false;
		}

		private bool TerminateIncomingState(IncomingKey key, DispatchContext context)
		{
			lock (_incomingGate)
			{
				if (_incoming.TryGetValue(key, out IncomingState state))
				{
					state.Terminated = true;
					state.Active?.Bytes.Dispose();
					state.Active = null;
				}
			}
			return TerminateIncoming(context);
		}

		private bool TerminateIncoming(DispatchContext context)
		{
			_terminateIncoming(context);
			return false;
		}

		private bool IsIncomingTerminated(IncomingKey key)
		{
			lock (_incomingGate)
				return _incoming.TryGetValue(key, out IncomingState state) && state.Terminated;
		}

		internal void DropConnection(object connection)
		{
			if (connection != null && _outgoing.TryGetValue(connection, out OutgoingState state))
			{
				FailOutgoing(connection, state, terminateConnection: false);
				_outgoing.Remove(connection);
			}
		}

		internal bool IsOutgoingTerminated(object connection)
			=> connection != null
			   && _outgoing.TryGetValue(connection, out OutgoingState state)
			   && state.Terminated;

		internal bool HasPendingReliable(object connection)
			=> connection != null
			   && _outgoing.TryGetValue(connection, out OutgoingState state)
			   && !state.Terminated
			   && (state.QueuedFrames > 0 || state.InFlightPages > 0);

		internal void DropIncoming(ulong senderId)
		{
			lock (_incomingGate)
			{
				foreach (IncomingKey key in _incoming.Keys.Where(key => key.SenderId == senderId).ToList())
				{
					_incoming[key].Active?.Bytes.Dispose();
					_incoming.Remove(key);
				}
			}
		}

		internal void Reset()
		{
			foreach (object connection in _outgoing.Keys.ToList())
				DropConnection(connection);
			lock (_incomingGate)
			{
				foreach (IncomingState state in _incoming.Values)
					state.Active?.Bytes.Dispose();
				_incoming.Clear();
			}
		}

		internal void ExpireStalledTransfers()
		{
			System.DateTime now = _utcNow();
			foreach (var pair in _outgoing.ToList())
			{
				OutgoingState state = pair.Value;
				if (!state.Terminated && state.InFlightPages > 0
				    && now - state.LastProgressUtc >= AckTimeout)
					TerminateOutgoing(pair.Key, state);
			}
		}

		internal int PendingFramesForTests(object connection)
			=> _outgoing.TryGetValue(connection, out OutgoingState state) ? state.Pending.Count : 0;

		internal int InFlightPagesForTests(object connection)
			=> _outgoing.TryGetValue(connection, out OutgoingState state) ? state.InFlightPages : 0;

		internal int QueuedFramesForTests(object connection)
			=> _outgoing.TryGetValue(connection, out OutgoingState state) ? state.QueuedFrames : 0;

	}
}
