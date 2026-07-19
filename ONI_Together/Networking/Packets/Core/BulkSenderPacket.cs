using Epic.OnlineServices.P2P;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DuplicantActions;
using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.Core
{
	internal class BulkSenderPacket : IPacket
	{
		public const int MaxPacketCount = 1024;
		public const int MaxInnerPacketBytes = 1024 * 1024;

		public BulkSenderPacket() { }
		public BulkSenderPacket(int packetId, List<byte[]> innerData)
		{
			using var _ = Profiler.Scope();

			InnerPacketId = packetId;
			SerializedInnerPackets = new List<byte[]>(innerData);
		}

		public int InnerPacketId;
		public List<byte[]> SerializedInnerPackets = [];

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			ValidatePayloads();
			writer.Write(InnerPacketId);
			int packetCount = SerializedInnerPackets.Count();
			writer.Write(packetCount);
			for (int i = 0; i < packetCount; i++)
			{
				var serializedPacket = SerializedInnerPackets[i];
				writer.Write(serializedPacket.Length);
				writer.Write(serializedPacket);
			}
			//DebugConsole.LogSuccess("Dispatching bulk packet of type " + PacketRegistry.Create(InnerPacketId).GetType().Name + " with " + SerializedInnerPackets.Count() + " packets innit");

		}
		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			InnerPacketId = reader.ReadInt32();
			int packetCount = reader.ReadInt32();
			if (packetCount <= 0 || packetCount > MaxPacketCount)
				throw new InvalidDataException($"Invalid bulk packet count: {packetCount}");
			SerializedInnerPackets = new List<byte[]>(packetCount);
			int totalBytes = 0;
			for (int i = 0; i < packetCount; i++)
			{
				int packetDataLength = reader.ReadInt32();
				if (packetDataLength < 0 || packetDataLength > MaxInnerPacketBytes)
					throw new InvalidDataException($"Invalid bulk inner length: {packetDataLength}");
				totalBytes = checked(totalBytes + packetDataLength);
				if (totalBytes > PacketHandler.MaxPacketSize)
					throw new InvalidDataException($"Bulk payload exceeds {PacketHandler.MaxPacketSize} bytes");
				if (reader.BaseStream.CanSeek && reader.BaseStream.Length - reader.BaseStream.Position < packetDataLength)
					throw new EndOfStreamException("Bulk inner payload is truncated");
				var packetData = reader.ReadBytes(packetDataLength);
				if (packetData.Length != packetDataLength)
					throw new EndOfStreamException("Bulk inner payload is truncated");
				SerializedInnerPackets.Add(packetData);
			}
			ValidatePayloads();
		}
		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (!PacketRegistry.HasRegisteredPacket(InnerPacketId))
				throw new InvalidDataException($"Unknown bulk inner packet id: {InnerPacketId}");
			DispatchContext context = PacketHandler.CurrentContext;
			if (!MultiplayerSession.IsHost && !context.SenderIsHost)
				throw new InvalidDataException("Client received bulk data from a non-host sender");

			var packetTemplate = PacketRegistry.Create(InnerPacketId);
			if (packetTemplate is not IBulkablePacket)
				throw new InvalidDataException($"{packetTemplate.GetType().Name} is not bulkable");
			if (MultiplayerSession.IsHost && packetTemplate is not IClientRelayable)
				throw new InvalidDataException($"Client cannot bulk-send {packetTemplate.GetType().Name}");

			foreach (var packetData in SerializedInnerPackets)
			{
				var innerPacket = PacketRegistry.Create(InnerPacketId);
				using var ms = new MemoryStream(packetData, writable: false);
				using var reader = new BinaryReader(ms);
				innerPacket.Deserialize(reader);
				if (reader.BaseStream.Position != reader.BaseStream.Length)
					throw new InvalidDataException("Bulk inner packet has trailing payload");
				if (!PacketHandler.DispatchNested(innerPacket, context))
					throw new InvalidDataException("Bulk inner packet dispatch was rejected");
			}
		}

		private void ValidatePayloads()
		{
			if (SerializedInnerPackets == null || SerializedInnerPackets.Count <= 0
			    || SerializedInnerPackets.Count > MaxPacketCount)
				throw new InvalidDataException("Invalid bulk packet count");
			int totalBytes = 0;
			foreach (byte[] packet in SerializedInnerPackets)
			{
				if (packet == null || packet.Length <= 0 || packet.Length > MaxInnerPacketBytes)
					throw new InvalidDataException("Invalid bulk inner length");
				totalBytes = checked(totalBytes + packet.Length);
				if (totalBytes > PacketHandler.MaxPacketSize)
					throw new InvalidDataException("Bulk payload exceeds its bound");
			}
		}
	}
}
