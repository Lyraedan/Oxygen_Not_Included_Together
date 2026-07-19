using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.Core
{
	internal sealed class DeferredReliableBatchPacket : IPacket, IHostOnlyPacket
	{
		internal const int MaxFrames = 4096;
		internal const int MaxSerializedBytes = ReliablePageChannel.MaxQueuedBytes - sizeof(int) * 3;
		private readonly List<byte[]> _frames = new();

		public DeferredReliableBatchPacket() { }

		internal DeferredReliableBatchPacket(IEnumerable<byte[]> frames)
		{
			if (frames != null)
				_frames.AddRange(frames);
			Validate();
		}

		internal int FrameCount => _frames.Count;

		public void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(_frames.Count);
			foreach (byte[] frame in _frames)
			{
				writer.Write(frame.Length);
				writer.Write(frame);
			}
		}

		public void Deserialize(BinaryReader reader)
		{
			int count = reader.ReadInt32();
			if (count <= 0 || count > MaxFrames)
				throw new InvalidDataException($"Invalid deferred reliable batch count: {count}");
			_frames.Clear();
			int bytes = sizeof(int);
			for (int index = 0; index < count; index++)
			{
				int length = reader.ReadInt32();
				if (length < sizeof(int) || length > MaxSerializedBytes - bytes - sizeof(int))
					throw new InvalidDataException($"Invalid deferred reliable frame length: {length}");
				byte[] frame = reader.ReadBytes(length);
				if (frame.Length != length)
					throw new EndOfStreamException("Deferred reliable batch is truncated");
				_frames.Add(frame);
				bytes += sizeof(int) + length;
			}
			Validate();
		}

		public void OnDispatched()
		{
			Validate();
			if (MultiplayerSession.IsHost || !PacketHandler.CurrentContext.SenderIsHost)
				throw new InvalidDataException("Deferred reliable batch has an invalid sender");
			foreach (byte[] frame in _frames)
			{
				if (!PacketHandler.TryHandleIncoming(frame, PacketHandler.CurrentContext))
					throw new InvalidDataException("Deferred reliable frame was rejected");
			}
		}

		private void Validate()
		{
			if (_frames.Count <= 0 || _frames.Count > MaxFrames)
				throw new InvalidDataException($"Invalid deferred reliable batch count: {_frames.Count}");
			int bytes = sizeof(int);
			foreach (byte[] frame in _frames)
			{
				if (frame == null || frame.Length < sizeof(int)
				    || ReliablePageChannel.IsForbiddenDeferredFrame(frame)
				    || frame.Length > MaxSerializedBytes - bytes - sizeof(int))
					throw new InvalidDataException("Deferred reliable batch exceeds its bound");
				bytes += sizeof(int) + frame.Length;
			}
		}
	}
}
