using System;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.Core
{
	internal sealed class DeferredReliablePacket : IPacket, IHostOnlyPacket
	{
		internal const int MaxPayloadBytes = 4 * 1024 * 1024;
		private byte[] _payload = Array.Empty<byte>();

		public DeferredReliablePacket() { }

		internal DeferredReliablePacket(byte[] payload)
		{
			_payload = payload ?? Array.Empty<byte>();
		}

		internal int PayloadLength => _payload.Length;

		public void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(_payload.Length);
			writer.Write(_payload);
		}

		public void Deserialize(BinaryReader reader)
		{
			int length = reader.ReadInt32();
			ValidateLength(length);
			_payload = reader.ReadBytes(length);
			if (_payload.Length != length)
				throw new EndOfStreamException("Deferred reliable packet is truncated");
			Validate();
		}

		public void OnDispatched()
		{
			if (MultiplayerSession.IsHost || !PacketHandler.CurrentContext.SenderIsHost)
				throw new InvalidDataException("Deferred reliable packet has an invalid sender");

			Validate();
			if (!PacketHandler.TryHandleIncoming(_payload, PacketHandler.CurrentContext))
				throw new InvalidDataException("Deferred reliable payload was rejected");
		}

		private void Validate()
		{
			ValidateLength(_payload.Length);
			if (ReliablePageChannel.IsForbiddenDeferredFrame(_payload))
				throw new InvalidDataException("Deferred reliable payload cannot contain an envelope");
		}

		private static void ValidateLength(int length)
		{
			if (length < sizeof(int) || length > MaxPayloadBytes)
				throw new InvalidDataException($"Invalid deferred reliable payload length: {length}");
		}
	}
}
