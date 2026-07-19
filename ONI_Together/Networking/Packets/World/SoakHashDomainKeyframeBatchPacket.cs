#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.World
{
	public sealed class SoakHashDomainKeyframeBatchPacket : IPacket, IHostOnlyPacket
	{
		internal const int MaxOrderedWireBytes =
			SoakHashDomainKeyframePagePacket.MaxOrderedWireBytes;
		internal const int MaxEntriesPerBatch = 128;
		private const int ReliableFrameFixedBytes = sizeof(int) * 7;
		private readonly List<byte[]> _entries = new();
		private int _reliableFrameBytes = ReliableFrameFixedBytes;

		public int RunId;
		public int SampleId;
		public int FirstEntryIndex;

		internal int EntryCount => _entries.Count;
		internal int NextEntryIndex => checked(FirstEntryIndex + EntryCount);
		internal IReadOnlyList<byte[]> Entries => _entries;

		internal static SoakHashDomainKeyframeBatchPacket Create(
			int runId, int sampleId, int firstEntryIndex)
		{
			SoakHashWire.ValidateMarker(runId, sampleId, 0f);
			if (firstEntryIndex < 0
			    || firstEntryIndex >= SoakHashDomainKeyframeBeginPacket.MaxEntries)
				throw new ArgumentOutOfRangeException(nameof(firstEntryIndex));
			return new SoakHashDomainKeyframeBatchPacket
			{
				RunId = runId,
				SampleId = sampleId,
				FirstEntryIndex = firstEntryIndex,
			};
		}

		internal bool TryAppend(byte[] entry)
		{
			if (entry == null || entry.Length <= 0
			    || EntryCount >= MaxEntriesPerBatch
			    || NextEntryIndex >= SoakHashDomainKeyframeBeginPacket.MaxEntries)
				return false;
			if (entry.Length > MaxOrderedWireBytes - sizeof(int))
				return false;
			int nextFrameBytes = checked(
				_reliableFrameBytes + sizeof(int) + entry.Length);
			if (OrderedWireBytesForFrame(nextFrameBytes) > MaxOrderedWireBytes)
				return false;
			_entries.Add(entry);
			_reliableFrameBytes = nextFrameBytes;
			return true;
		}

		public void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(RunId);
			writer.Write(SampleId);
			writer.Write(FirstEntryIndex);
			writer.Write(EntryCount);
			foreach (byte[] entry in _entries)
			{
				writer.Write(entry.Length);
				writer.Write(entry);
			}
		}

		public void Deserialize(BinaryReader reader)
		{
			RunId = reader.ReadInt32();
			SampleId = reader.ReadInt32();
			FirstEntryIndex = reader.ReadInt32();
			int count = reader.ReadInt32();
			if (count <= 0 || count > MaxEntriesPerBatch)
				throw new InvalidDataException("Invalid soak keyframe batch count");
			_entries.Clear();
			_reliableFrameBytes = ReliableFrameFixedBytes;
			for (int index = 0; index < count; index++)
			{
				int length = reader.ReadInt32();
				if (length <= 0 || length > MaxOrderedWireBytes - sizeof(int))
					throw new InvalidDataException("Invalid soak keyframe batch entry length");
				int nextFrameBytes = checked(
					_reliableFrameBytes + sizeof(int) + length);
				if (OrderedWireBytesForFrame(nextFrameBytes) > MaxOrderedWireBytes)
					throw new InvalidDataException("Soak keyframe batch exceeds wire bounds");
				byte[] entry = reader.ReadBytes(length);
				if (entry.Length != length)
					throw new EndOfStreamException("Soak keyframe batch is truncated");
				_entries.Add(entry);
				_reliableFrameBytes = nextFrameBytes;
			}
			Validate();
		}

		public void OnDispatched()
		{
			if (MultiplayerSession.IsHost)
				return;
			SoakKeyframeBatchReceiveResult result = SoakKeyframePageReceiver.Accept(this);
			if (result is not (SoakKeyframeBatchReceiveResult.Advanced
			    or SoakKeyframeBatchReceiveResult.StreamComplete)
			    || !SoakKeyframePageReceiver.TryGetPendingBatchAck(out var progress)
			    || !PacketSender.SendToHost(progress, PacketSendMode.ReliableImmediate))
				return;
			SoakKeyframePageReceiver.CommitAck(progress);
		}

		internal int GetOrderedReliableWireBytes()
		{
			Validate();
			return OrderedWireBytesForFrame(_reliableFrameBytes);
		}

		internal int GetOuterReliablePageCount()
			=> ReliablePageChannel.PageCount(_reliableFrameBytes);

		internal bool IsValid()
		{
			try
			{
				Validate();
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		private void Validate()
		{
			SoakHashWire.ValidateMarker(RunId, SampleId, 0f);
			if (FirstEntryIndex < 0 || EntryCount <= 0
			    || EntryCount > MaxEntriesPerBatch
			    || NextEntryIndex > SoakHashDomainKeyframeBeginPacket.MaxEntries)
				throw new InvalidDataException("Invalid soak keyframe batch metadata");
			int frameBytes = ReliableFrameFixedBytes;
			foreach (byte[] entry in _entries)
			{
				if (entry == null || entry.Length <= 0)
					throw new InvalidDataException("Invalid soak keyframe batch entry");
				frameBytes = checked(frameBytes + sizeof(int) + entry.Length);
			}
			if (frameBytes != _reliableFrameBytes
			    || OrderedWireBytesForFrame(frameBytes) > MaxOrderedWireBytes)
				throw new InvalidDataException("Soak keyframe batch exceeds wire bounds");
		}

		private static int OrderedWireBytesForFrame(int frameBytes)
			=> checked(frameBytes + ReliablePageChannel.PageCount(frameBytes)
			   * SoakHashDomainKeyframePagePacket.OrderedWireOverheadBytes);
	}
}
#endif
