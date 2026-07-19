using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;

namespace ONI_Together.Networking
{
	internal sealed partial class ReliablePageChannel
	{
		internal static bool IsTransportEnvelopeData(byte[] frame)
		{
			if (frame == null || frame.Length < sizeof(int))
				return false;
			int packetId = BitConverter.ToInt32(frame, 0);
			return packetId == API_Helper.GetHashCode(typeof(OrderedReliablePacket))
			       || packetId == API_Helper.GetHashCode(typeof(ReliablePagePacket))
			       || packetId == API_Helper.GetHashCode(typeof(ReliablePageAckPacket))
			       || packetId == API_Helper.GetHashCode(typeof(ChunkedPacket));
		}

		internal static bool IsForbiddenDeferredFrame(byte[] frame)
			=> IsTransportEnvelopeData(frame)
			   || frame != null && frame.Length >= sizeof(int)
			   && (BitConverter.ToInt32(frame, 0)
			       == API_Helper.GetHashCode(typeof(DeferredReliableBatchPacket))
			       || BitConverter.ToInt32(frame, 0)
			       == API_Helper.GetHashCode(typeof(DeferredReliablePacket)));

		internal static bool IsForbiddenDedicatedFrame(byte[] frame)
			=> IsForbiddenDeferredFrame(frame)
			   || frame != null && frame.Length >= sizeof(int)
			   && BitConverter.ToInt32(frame, 0)
			   == API_Helper.GetHashCode(typeof(DedicatedServerMessagePacket));

		private static bool TryDecodeBatch(byte[] batch, out List<byte[]> frames)
		{
			frames = new List<byte[]>();
			try
			{
				using var stream = new MemoryStream(batch, writable: false);
				using var reader = new BinaryReader(stream);
				int count = reader.ReadInt32();
				if (count <= 0 || count > MaxQueuedFrames)
					return false;
				for (int index = 0; index < count; index++)
				{
					int length = reader.ReadInt32();
					if (length < sizeof(int) || length > stream.Length - stream.Position)
						return false;
					frames.Add(reader.ReadBytes(length));
				}
				return stream.Position == stream.Length;
			}
			catch
			{
				frames.Clear();
				return false;
			}
		}

		internal static int PageCount(int totalBytes)
			=> totalBytes <= 0 ? 0 : (totalBytes - 1) / MaxPageDataBytes + 1;

		internal static int PageLength(int totalBytes, int pageIndex)
			=> Math.Min(MaxPageDataBytes, totalBytes - pageIndex * MaxPageDataBytes);

		private readonly struct IncomingKey : IEquatable<IncomingKey>
		{
			internal IncomingKey(DispatchContext context)
			{
				SenderId = context.SenderId;
				Generation = context.ConnectionGeneration;
				Epoch = context.SessionEpoch;
			}

			internal ulong SenderId { get; }
			private long Generation { get; }
			private long Epoch { get; }
			public bool Equals(IncomingKey other)
				=> SenderId == other.SenderId && Generation == other.Generation && Epoch == other.Epoch;
			public override bool Equals(object obj) => obj is IncomingKey other && Equals(other);
			public override int GetHashCode()
				=> (SenderId.GetHashCode() * 397 ^ Generation.GetHashCode()) * 397 ^ Epoch.GetHashCode();
		}
	}
}
