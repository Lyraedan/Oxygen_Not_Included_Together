#if DEBUG
using System;
using System.Collections.Generic;
using ONI_Together.Networking.Packets.Architecture;

namespace ONI_Together.Networking.Packets.World
{
	internal enum SoakKeyframePageReceiveResult
	{
		Ignore,
		Invalid,
		Advanced,
		EntryComplete,
		StreamComplete,
	}

	internal enum SoakKeyframeBatchReceiveResult
	{
		Ignore,
		Invalid,
		Advanced,
		StreamComplete,
	}

	internal static class SoakKeyframePageReceiver
	{
		private static int _runId;
		private static int _sampleId;
		private static int _expectedEntries;
		private static int _entryIndex;
		private static int _nextPageIndex;
		private static int _pageCount;
		private static int _totalBytes;
		private static int _writtenBytes;
		private static byte[] _buffer;
		private static SoakKeyframePageAckPacket _pendingAck;
		private static SoakKeyframeBatchAckPacket _pendingBatchAck;

		internal static int BufferedBytes => _buffer?.Length ?? 0;
		internal static int CompletedEntries => _entryIndex;

		internal static void Begin(int runId, int sampleId, int expectedEntries)
		{
			Reset();
			if (runId <= 0 || sampleId <= 0 || expectedEntries <= 0
			    || expectedEntries > SoakHashDomainKeyframeBeginPacket.MaxEntries)
				return;
			_runId = runId;
			_sampleId = sampleId;
			_expectedEntries = expectedEntries;
		}

		internal static SoakKeyframePageReceiveResult Accept(
			SoakHashDomainKeyframePagePacket page)
		{
			if (page == null || !page.IsValid())
				return SoakKeyframePageReceiveResult.Invalid;
			if (_runId == 0 || page.RunId != _runId || page.SampleId != _sampleId)
				return SoakKeyframePageReceiveResult.Ignore;
			if (page.EntryIndex < _entryIndex
			    || page.EntryIndex == _entryIndex && page.PageIndex < _nextPageIndex)
				return SoakKeyframePageReceiveResult.Ignore;
			if (page.EntryIndex != _entryIndex || page.PageIndex != _nextPageIndex
			    || page.EntryIndex >= _expectedEntries
			    || _pendingAck != null || _pendingBatchAck != null)
				return SoakKeyframePageReceiveResult.Invalid;
			if (!PrepareOrMatchEntry(page))
				return Fail();

			Buffer.BlockCopy(page.Payload, 0, _buffer, _writtenBytes, page.Payload.Length);
			_writtenBytes += page.Payload.Length;
			_nextPageIndex++;
			bool entryComplete = _nextPageIndex == _pageCount;
			if (!entryComplete)
			{
				_pendingAck = CreateAck(page, false);
				return SoakKeyframePageReceiveResult.Advanced;
			}
			return CompleteEntry(page);
		}

		internal static SoakKeyframeBatchReceiveResult Accept(
			SoakHashDomainKeyframeBatchPacket batch)
		{
			if (batch == null || !batch.IsValid())
				return SoakKeyframeBatchReceiveResult.Invalid;
			if (_runId == 0 || batch.RunId != _runId || batch.SampleId != _sampleId)
				return SoakKeyframeBatchReceiveResult.Ignore;
			if (batch.NextEntryIndex <= _entryIndex)
				return SoakKeyframeBatchReceiveResult.Ignore;
			if (batch.FirstEntryIndex != _entryIndex
			    || batch.NextEntryIndex > _expectedEntries
			    || _buffer != null || _pendingAck != null || _pendingBatchAck != null)
				return FailBatch();

			var packets = new List<SoakHashDomainKeyframePacket>(batch.EntryCount);
			try
			{
				for (int index = 0; index < batch.EntryCount; index++)
				{
					SoakHashDomainKeyframePacket packet =
						SoakHashDomainKeyframePacket.DeserializeBody(batch.Entries[index]);
					if (packet.RunId != _runId || packet.SampleId != _sampleId
					    || packet.EntryIndex != _entryIndex + index)
						return FailBatch();
					packets.Add(packet);
				}
			}
			catch (Exception)
			{
				return FailBatch();
			}
			if (!SoakHashDomainKeyframeTracker.RecordPagedBatch(packets))
				return FailBatch();

			int firstEntry = _entryIndex;
			_entryIndex = batch.NextEntryIndex;
			bool streamComplete = _entryIndex == _expectedEntries;
			_pendingBatchAck = new SoakKeyframeBatchAckPacket
			{
				RunId = _runId,
				SampleId = _sampleId,
				FirstEntryIndex = firstEntry,
				ReceivedEntries = _entryIndex,
				ApplyFinished = streamComplete,
				ApplySucceeded = streamComplete
				                 && SoakHashDomainKeyframeTracker.ApplySucceeded(
					                 _runId, _sampleId),
			};
			return streamComplete
				? SoakKeyframeBatchReceiveResult.StreamComplete
				: SoakKeyframeBatchReceiveResult.Advanced;
		}

		private static bool PrepareOrMatchEntry(SoakHashDomainKeyframePagePacket page)
		{
			if (_buffer != null)
				return page.PageCount == _pageCount && page.TotalBytes == _totalBytes
			       && _writtenBytes == checked(page.PageIndex
			                                  * SoakHashDomainKeyframePagePacket.MaxPayloadBytes);
			if (page.PageIndex != 0 || page.TotalBytes > PacketHandler.MaxPacketSize)
				return false;
			_pageCount = page.PageCount;
			_totalBytes = page.TotalBytes;
			_writtenBytes = 0;
			_buffer = new byte[_totalBytes];
			return true;
		}

		private static SoakKeyframePageReceiveResult CompleteEntry(
			SoakHashDomainKeyframePagePacket page)
		{
			if (_writtenBytes != _totalBytes)
				return Fail();
			SoakHashDomainKeyframePacket packet;
			try
			{
				packet = SoakHashDomainKeyframePacket.DeserializeBody(_buffer);
			}
			catch (Exception)
			{
				return Fail();
			}
			if (packet.RunId != _runId || packet.SampleId != _sampleId
			    || packet.EntryIndex != _entryIndex
			    || !SoakHashDomainKeyframeTracker.RecordPagedPacket(packet))
				return Fail();

			_entryIndex++;
			bool streamComplete = _entryIndex == _expectedEntries;
			_pendingAck = CreateAck(page, streamComplete);
			ClearEntryBuffer();
			return streamComplete
				? SoakKeyframePageReceiveResult.StreamComplete
				: SoakKeyframePageReceiveResult.EntryComplete;
		}

		private static SoakKeyframePageAckPacket CreateAck(
			SoakHashDomainKeyframePagePacket page, bool streamComplete)
			=> new SoakKeyframePageAckPacket
			{
				RunId = _runId,
				SampleId = _sampleId,
				EntryIndex = page.EntryIndex,
				AcknowledgedPages = page.PageIndex + 1,
				ReceivedEntries = _entryIndex,
				ApplyFinished = streamComplete,
				ApplySucceeded = streamComplete
				                 && SoakHashDomainKeyframeTracker.ApplySucceeded(
					                 _runId, _sampleId),
			};

		internal static bool TryGetPendingAck(out SoakKeyframePageAckPacket ack)
		{
			ack = _pendingAck;
			return ack != null;
		}

		internal static void CommitAck(SoakKeyframePageAckPacket ack)
		{
			if (_pendingAck?.Matches(ack) == true)
				_pendingAck = null;
		}

		internal static bool TryGetPendingBatchAck(out SoakKeyframeBatchAckPacket ack)
		{
			ack = _pendingBatchAck;
			return ack != null;
		}

		internal static void CommitAck(SoakKeyframeBatchAckPacket ack)
		{
			if (_pendingBatchAck?.Matches(ack) == true)
				_pendingBatchAck = null;
		}

		private static SoakKeyframePageReceiveResult Fail()
		{
			Reset();
			return SoakKeyframePageReceiveResult.Invalid;
		}

		private static SoakKeyframeBatchReceiveResult FailBatch()
		{
			Reset();
			return SoakKeyframeBatchReceiveResult.Invalid;
		}

		private static void ClearEntryBuffer()
		{
			_buffer = null;
			_pageCount = 0;
			_totalBytes = 0;
			_writtenBytes = 0;
			_nextPageIndex = 0;
		}

		internal static void Reset()
		{
			_runId = 0;
			_sampleId = 0;
			_expectedEntries = 0;
			_entryIndex = 0;
			_pendingAck = null;
			_pendingBatchAck = null;
			ClearEntryBuffer();
		}
	}
}
#endif
