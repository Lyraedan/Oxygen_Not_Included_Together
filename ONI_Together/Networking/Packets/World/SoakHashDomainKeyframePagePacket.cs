#if DEBUG
using System;
using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.World
{
	public sealed class SoakHashDomainKeyframePagePacket : IPacket, IHostOnlyPacket
	{
		internal const int RiptideFragmentPayloadBytes = 980;
		internal const int MaxRiptideFragmentsPerPage = 12;
		internal const int MaxOrderedWireBytes =
			RiptideFragmentPayloadBytes * MaxRiptideFragmentsPerPage;
		internal const int OrderedWireOverheadBytes = sizeof(int) * 10 + sizeof(long);
		internal const int MaxPayloadBytes = MaxOrderedWireBytes - OrderedWireOverheadBytes;
		internal const int MaxEntryBytes = PacketHandler.MaxPacketSize;
		internal const int MaxPagesPerEntry =
			(MaxEntryBytes + MaxPayloadBytes - 1) / MaxPayloadBytes;

		public int RunId;
		public int SampleId;
		public int EntryIndex;
		public int PageIndex;
		public int PageCount;
		public int TotalBytes;
		public byte[] Payload = Array.Empty<byte>();

		public void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(RunId);
			writer.Write(SampleId);
			writer.Write(EntryIndex);
			writer.Write(PageIndex);
			writer.Write(PageCount);
			writer.Write(TotalBytes);
			writer.Write(Payload.Length);
			writer.Write(Payload);
		}

		public void Deserialize(BinaryReader reader)
		{
			RunId = reader.ReadInt32();
			SampleId = reader.ReadInt32();
			EntryIndex = reader.ReadInt32();
			PageIndex = reader.ReadInt32();
			PageCount = reader.ReadInt32();
			TotalBytes = reader.ReadInt32();
			int payloadLength = reader.ReadInt32();
			if (payloadLength <= 0 || payloadLength > MaxPayloadBytes)
				throw new InvalidDataException("Invalid soak keyframe page length");
			Payload = reader.ReadBytes(payloadLength);
			if (Payload.Length != payloadLength)
				throw new EndOfStreamException("Soak keyframe page is truncated");
			Validate();
		}

		public void OnDispatched()
		{
			if (MultiplayerSession.IsHost)
				return;
			SoakKeyframePageReceiveResult result = SoakKeyframePageReceiver.Accept(this);
			if (result is SoakKeyframePageReceiveResult.Advanced
			    or SoakKeyframePageReceiveResult.EntryComplete
			    or SoakKeyframePageReceiveResult.StreamComplete)
				SoakStateHashProbe.SendKeyframeProgress();
		}

		internal static int PageCountFor(int totalBytes)
		{
			if (totalBytes <= 0 || totalBytes > MaxEntryBytes)
				throw new ArgumentOutOfRangeException(nameof(totalBytes));
			return (totalBytes + MaxPayloadBytes - 1) / MaxPayloadBytes;
		}

		internal static SoakHashDomainKeyframePagePacket Create(
			int runId, int sampleId, int entryIndex, int pageIndex, byte[] entryBytes)
		{
			if (entryBytes == null)
				throw new ArgumentNullException(nameof(entryBytes));
			int pageCount = PageCountFor(entryBytes.Length);
			if (pageIndex < 0 || pageIndex >= pageCount)
				throw new ArgumentOutOfRangeException(nameof(pageIndex));
			int offset = checked(pageIndex * MaxPayloadBytes);
			int length = Math.Min(MaxPayloadBytes, entryBytes.Length - offset);
			var payload = new byte[length];
			Buffer.BlockCopy(entryBytes, offset, payload, 0, length);
			var page = new SoakHashDomainKeyframePagePacket
			{
				RunId = runId,
				SampleId = sampleId,
				EntryIndex = entryIndex,
				PageIndex = pageIndex,
				PageCount = pageCount,
				TotalBytes = entryBytes.Length,
				Payload = payload,
			};
			page.Validate();
			return page;
		}

		internal int GetOrderedReliableWireBytes()
		{
			Validate();
			return checked(OrderedWireOverheadBytes + Payload.Length);
		}

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
			if (EntryIndex < 0
			    || EntryIndex >= SoakHashDomainKeyframeBeginPacket.MaxEntries
			    || TotalBytes <= 0 || TotalBytes > MaxEntryBytes
			    || PageCount != PageCountFor(TotalBytes)
			    || PageCount > MaxPagesPerEntry
			    || PageIndex < 0 || PageIndex >= PageCount
			    || Payload == null)
				throw new InvalidDataException("Invalid soak keyframe page metadata");
			int expectedLength = Math.Min(
				MaxPayloadBytes, TotalBytes - checked(PageIndex * MaxPayloadBytes));
			if (Payload.Length != expectedLength
			    || GetUncheckedOrderedReliableWireBytes() > MaxOrderedWireBytes)
				throw new InvalidDataException("Invalid soak keyframe page payload");
		}

		private int GetUncheckedOrderedReliableWireBytes()
			=> checked(OrderedWireOverheadBytes + (Payload?.Length ?? 0));
	}
}
#endif
