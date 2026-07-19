using System;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;

namespace ONI_Together.Networking.Packets.Core
{
	internal interface IReliablePageControl { }

	internal sealed class ReliablePagePacket : IPacket, IReliablePageControl
	{
		public ReliablePagePacket() { }

		internal ReliablePagePacket(
			long transferId, int pageIndex, int totalPages, int totalBytes, byte[] data)
		{
			TransferId = transferId;
			PageIndex = pageIndex;
			TotalPages = totalPages;
			TotalBytes = totalBytes;
			Data = data ?? Array.Empty<byte>();
		}

		internal long TransferId { get; private set; }
		internal int PageIndex { get; private set; }
		internal int TotalPages { get; private set; }
		internal int TotalBytes { get; private set; }
		internal byte[] Data { get; private set; } = Array.Empty<byte>();

		public void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(TransferId);
			writer.Write(PageIndex);
			writer.Write(TotalPages);
			writer.Write(TotalBytes);
			writer.Write(Data.Length);
			writer.Write(Data);
		}

		public void Deserialize(BinaryReader reader)
		{
			TransferId = reader.ReadInt64();
			PageIndex = reader.ReadInt32();
			TotalPages = reader.ReadInt32();
			TotalBytes = reader.ReadInt32();
			int length = reader.ReadInt32();
			if (length < 0 || length > ReliablePageChannel.MaxPageDataBytes)
				throw new InvalidDataException($"Invalid reliable page length: {length}");
			Data = reader.ReadBytes(length);
			if (Data.Length != length)
				throw new EndOfStreamException("Reliable page is truncated");
			Validate();
		}

		public void OnDispatched()
		{
			if (!PacketSender.AcceptReliablePage(this, PacketHandler.CurrentContext))
				throw new InvalidDataException("Reliable page application was rejected");
		}

		private void Validate()
		{
			if (TransferId <= 0 || TotalBytes < sizeof(int) * 2
			    || TotalBytes > ReliablePageChannel.MaxQueuedBytes
			    || TotalPages != ReliablePageChannel.PageCount(TotalBytes)
			    || PageIndex < 0 || PageIndex >= TotalPages
			    || Data == null
			    || Data.Length != ReliablePageChannel.PageLength(TotalBytes, PageIndex))
				throw new InvalidDataException("Invalid reliable page metadata");
		}
	}

	internal sealed class ReliablePageAckPacket : IPacket, IReliablePageControl
	{
		public ReliablePageAckPacket() { }

		internal ReliablePageAckPacket(
			long transferId, int pageIndex, int totalPages, int totalBytes)
		{
			TransferId = transferId;
			PageIndex = pageIndex;
			TotalPages = totalPages;
			TotalBytes = totalBytes;
		}

		internal long TransferId { get; private set; }
		internal int PageIndex { get; private set; }
		internal int TotalPages { get; private set; }
		internal int TotalBytes { get; private set; }

		public void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(TransferId);
			writer.Write(PageIndex);
			writer.Write(TotalPages);
			writer.Write(TotalBytes);
		}

		public void Deserialize(BinaryReader reader)
		{
			TransferId = reader.ReadInt64();
			PageIndex = reader.ReadInt32();
			TotalPages = reader.ReadInt32();
			TotalBytes = reader.ReadInt32();
			Validate();
		}

		public void OnDispatched()
		{
			if (!PacketSender.AcceptReliablePageAck(this, PacketHandler.CurrentContext))
				throw new InvalidDataException("Reliable page ACK was rejected");
		}

		private void Validate()
		{
			if (TransferId <= 0 || TotalBytes < sizeof(int) * 2
			    || TotalBytes > ReliablePageChannel.MaxQueuedBytes
			    || TotalPages != ReliablePageChannel.PageCount(TotalBytes)
			    || PageIndex < 0 || PageIndex >= TotalPages)
				throw new InvalidDataException("Invalid reliable page ACK metadata");
		}
	}
}
