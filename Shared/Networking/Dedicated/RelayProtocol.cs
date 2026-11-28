#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Shared.Networking.Dedicated
{
	public static class RelayProtocol
	{
		public const string ConnectionKey = "ONI_TOGETHER";

		public const string RejectKey = "ONI_RELAY_REJECT";
		public const int Version = 1;

		public const int Magic = 0x52594C52;

		public const int HeaderSize = 5;
		public const int PeerHeaderSize = HeaderSize + 8;

		public const int SaveChunkBytes = 32 * 1024;

		public const byte SaveChannel = 2;

		private const int MaxDlcEntries = 64;
		private const int MaxNameLength = 128;

		public static bool IsFrame(byte[]? data)
		{
			return data != null && data.Length >= HeaderSize && BitConverter.ToInt32(data, 0) == Magic;
		}

		public static RelayFrameType TypeOf(byte[] frame) => (RelayFrameType)frame[4];

		private static byte[] Build(RelayFrameType type, Action<BinaryWriter>? body = null)
		{
			using var ms = new MemoryStream();
			using (var writer = new BinaryWriter(ms, Encoding.UTF8, true))
			{
				writer.Write(Magic);
				writer.Write((byte)type);
				body?.Invoke(writer);
			}
			return ms.ToArray();
		}

		public static BinaryReader ReadBody(byte[] frame)
		{
			return new BinaryReader(new MemoryStream(frame, HeaderSize, frame.Length - HeaderSize, false), Encoding.UTF8);
		}

		public static byte[] MasterAssigned(bool hasSave, string saveName, int saveSize)
		{
			return Build(RelayFrameType.MasterAssigned, w =>
			{
				w.Write(Version);
				w.Write(hasSave);
				w.Write(saveName ?? string.Empty);
				w.Write(saveSize);
			});
		}

		public static byte[] Signal(RelayFrameType type) => Build(type);

		public static byte[] PeerEvent(RelayFrameType type, ulong peerId) => Build(type, w => w.Write(peerId));

		public static byte[] PeerData(RelayFrameType type, ulong peerId, byte[] payload, int count)
		{
			return Build(type, w =>
			{
				w.Write(peerId);
				w.Write(payload, 0, count);
			});
		}

		public static byte[] PeerData(RelayFrameType type, ulong peerId, byte[] payload) => PeerData(type, peerId, payload, payload.Length);

		public static bool TryReadPeerData(byte[] frame, out ulong peerId, out byte[] payload)
		{
			peerId = 0;
			payload = Array.Empty<byte>();
			if (frame.Length < PeerHeaderSize)
				return false;

			peerId = BitConverter.ToUInt64(frame, HeaderSize);
			payload = new byte[frame.Length - PeerHeaderSize];
			Buffer.BlockCopy(frame, PeerHeaderSize, payload, 0, payload.Length);
			return true;
		}

		public static IEnumerable<byte[]> SaveFrames(string name, byte[] data)
		{
			yield return Build(RelayFrameType.SaveBegin, w =>
			{
				w.Write(name ?? string.Empty);
				w.Write(data.Length);
			});

			for (int offset = 0; offset < data.Length; offset += SaveChunkBytes)
			{
				int start = offset;
				int length = Math.Min(SaveChunkBytes, data.Length - start);
				yield return Build(RelayFrameType.SaveData, w =>
				{
					w.Write(start);
					w.Write(data, start, length);
				});
			}
		}

		public static string SanitizeColonyName(string? name)
		{
			var result = new StringBuilder();
			foreach (char c in name ?? string.Empty)
			{
				if (c < 32 || "<>:\"/\\|?*".IndexOf(c) >= 0)
					continue;
				result.Append(c);
				if (result.Length >= MaxNameLength)
					break;
			}

			string cleaned = result.ToString().Trim().Trim('.').Trim();
			return cleaned.Length == 0 ? "Dedicated Colony" : cleaned;
		}

		public sealed class Hello
		{
			public ulong InstanceToken;
			public int ProtocolVersion = Version;
			public List<string> ActiveDlcIds = new List<string>();
			public List<string> OwnedDlcIds = new List<string>();

			public byte[] ToBytes()
			{
				using var ms = new MemoryStream();
				using (var writer = new BinaryWriter(ms, Encoding.UTF8, true))
				{
					writer.Write(InstanceToken);
					writer.Write(ProtocolVersion);
					WriteList(writer, ActiveDlcIds);
					WriteList(writer, OwnedDlcIds);
				}
				return ms.ToArray();
			}

			public static bool TryParse(byte[] bytes, out Hello hello)
			{
				hello = new Hello();
				try
				{
					using var reader = new BinaryReader(new MemoryStream(bytes, false), Encoding.UTF8);
					hello.InstanceToken = reader.ReadUInt64();
					hello.ProtocolVersion = reader.ReadInt32();
					return TryReadList(reader, hello.ActiveDlcIds) && TryReadList(reader, hello.OwnedDlcIds);
				}
				catch (IOException)
				{
					return false;
				}
				catch (FormatException)
				{
					return false;
				}
			}

			private static void WriteList(BinaryWriter writer, List<string> values)
			{
				int count = Math.Min(values.Count, MaxDlcEntries);
				writer.Write(count);
				for (int i = 0; i < count; i++)
					writer.Write(values[i] ?? string.Empty);
			}

			private static bool TryReadList(BinaryReader reader, List<string> target)
			{
				int count = reader.ReadInt32();
				if (count < 0 || count > MaxDlcEntries)
					return false;
				for (int i = 0; i < count; i++)
					target.Add(reader.ReadString());
				return true;
			}
		}
	}

	public enum RelayRejectCode
	{
		Other = 0,
		ServerFull = 1,
		HostLoading = 2,
		EnableSpacedOut = 3,
		NeedsSpacedOut = 4,
		DisableSpacedOut = 5,
		MissingDlc = 6,
		DlcPolicy = 7,
	}

	public enum RelayFrameType : byte
	{
		MasterAssigned = 1,
		HostReady = 2,
		PeerJoined = 3,
		PeerLeft = 4,
		FromPeer = 5,
		ToPeer = 6,
		KickPeer = 7,
		SaveRequest = 8,
		SaveBegin = 9,
		SaveData = 10,
		HostAborted = 11,
	}

	public sealed class RelaySaveAssembler
	{
		private readonly int _maxBytes;
		private byte[]? _buffer;
		private string _name = string.Empty;
		private int _received;

		public RelaySaveAssembler(int maxBytes)
		{
			_maxBytes = maxBytes;
		}

		public bool InProgress => _buffer != null;

		public void Reset()
		{
			_buffer = null;
			_name = string.Empty;
			_received = 0;
		}

		public bool Accept(byte[] frame, out string name, out byte[] data)
		{
			name = string.Empty;
			data = Array.Empty<byte>();

			try
			{
				using var reader = RelayProtocol.ReadBody(frame);

				if (RelayProtocol.TypeOf(frame) == RelayFrameType.SaveBegin)
				{
					Reset();
					string announced = reader.ReadString();
					int size = reader.ReadInt32();
					if (size <= 0 || size > _maxBytes)
						return false;

					_name = RelayProtocol.SanitizeColonyName(announced);
					_buffer = new byte[size];
					return false;
				}

				if (RelayProtocol.TypeOf(frame) != RelayFrameType.SaveData || _buffer == null)
					return false;

				int offset = reader.ReadInt32();
				int length = (int)(reader.BaseStream.Length - reader.BaseStream.Position);
				if (offset != _received || length <= 0 || length > _buffer.Length - _received)
				{
					Reset();
					return false;
				}

				reader.Read(_buffer, offset, length);
				_received += length;
				if (_received < _buffer.Length)
					return false;

				name = _name;
				data = _buffer;
				Reset();
				return true;
			}
			catch (IOException)
			{
				Reset();
				return false;
			}
		}
	}
}
