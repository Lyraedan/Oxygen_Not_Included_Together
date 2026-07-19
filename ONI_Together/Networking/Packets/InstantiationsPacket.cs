using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets
{
	public class InstantiationsPacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
	{
		internal const int MaxCompressedBytes = 16 * 1024 * 1024;
		internal const int MaxDecompressedBytes = 16 * 1024 * 1024;
		internal const int MaxInstantiationCount = 8192;
		private const int MaxNameLength = 256;

		public List<InstantiationEntry> Entries = new List<InstantiationEntry>();

		public struct InstantiationEntry
		{
			public string PrefabName;
			public Vector3 Position;
			public Quaternion Rotation;
			public string ObjectName;
			public bool InitializeId;
			public int GameLayer;
		}

		public void Serialize(BinaryWriter w)
		{
			using var _ = Profiler.Scope();

			using (var ms = new MemoryStream())
			{
				using (var deflate = new DeflateStream(ms, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
				{
					using (var tempWriter = new BinaryWriter(deflate))
					{
						tempWriter.Write(Entries.Count);
						foreach (var e in Entries)
						{
							tempWriter.Write(e.PrefabName ?? "");
							tempWriter.Write(e.Position.x); tempWriter.Write(e.Position.y); tempWriter.Write(e.Position.z);
							tempWriter.Write(e.Rotation.x); tempWriter.Write(e.Rotation.y); tempWriter.Write(e.Rotation.z); tempWriter.Write(e.Rotation.w);
							tempWriter.Write(e.ObjectName ?? "");
							tempWriter.Write(e.InitializeId);
							tempWriter.Write(e.GameLayer);
						}
					}
				}

				byte[] compressed = ms.ToArray();
				w.Write(compressed.Length);
				w.Write(compressed);
			}
		}

		public void Deserialize(BinaryReader r)
		{
			using var _ = Profiler.Scope();

			int compressedLength = r.ReadInt32();
			if (compressedLength <= 0 || compressedLength > MaxCompressedBytes)
				throw new InvalidDataException($"Invalid instantiations payload length: {compressedLength}");
			byte[] compressedData = r.ReadBytes(compressedLength);
			if (compressedData.Length != compressedLength)
				throw new EndOfStreamException("Instantiations payload is truncated");

			using (var ms = new MemoryStream(Decompress(compressedData)))
			{
				using (var tempReader = new BinaryReader(ms))
				{
					int count = tempReader.ReadInt32();
					if (count < 0 || count > MaxInstantiationCount)
						throw new InvalidDataException($"Invalid instantiation count: {count}");
					Entries = new List<InstantiationEntry>(count);

					for (int i = 0; i < count; i++)
					{
						string prefabName = tempReader.ReadString();
						if (prefabName.Length > MaxNameLength)
							throw new InvalidDataException("Instantiation prefab name is too long");
						var position = new Vector3(tempReader.ReadSingle(), tempReader.ReadSingle(), tempReader.ReadSingle());
						var rotation = new Quaternion(tempReader.ReadSingle(), tempReader.ReadSingle(), tempReader.ReadSingle(), tempReader.ReadSingle());
						string objectName = tempReader.ReadString();
						if (objectName.Length > MaxNameLength)
							throw new InvalidDataException("Instantiation object name is too long");
						Entries.Add(new InstantiationEntry
						{
							PrefabName = prefabName,
							Position = position,
							Rotation = rotation,
							ObjectName = objectName,
							InitializeId = tempReader.ReadBoolean(),
							GameLayer = tempReader.ReadInt32()
						});
					}
					if (tempReader.BaseStream.Position != tempReader.BaseStream.Length)
						throw new InvalidDataException("Instantiations payload contains trailing bytes");
				}
			}
		}

		private static byte[] Decompress(byte[] compressedData)
		{
			using var input = new MemoryStream(compressedData);
			using var deflate = new DeflateStream(input, CompressionMode.Decompress);
			using var output = new MemoryStream();
			var buffer = new byte[8192];
			int total = 0;
			while (true)
			{
				int remaining = MaxDecompressedBytes - total;
				int read = deflate.Read(buffer, 0, remaining < buffer.Length ? remaining + 1 : buffer.Length);
				if (read == 0)
					return output.ToArray();
				total += read;
				if (total > MaxDecompressedBytes)
					throw new InvalidDataException($"Instantiations payload expands beyond {MaxDecompressedBytes} bytes");
				output.Write(buffer, 0, read);
			}
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost) return;

			foreach (var e in Entries)
				Instantiate(e);
		}

		private void Instantiate(InstantiationEntry e)
		{
			using var _ = Profiler.Scope();

			GameObject prefab = Assets.GetPrefab(e.PrefabName);
			if (prefab == null)
			{
				DebugConsole.LogWarning($"[InstantiationsPacket] Missing prefab '{e.PrefabName}'");
				return;
			}

			GameObject obj = Object.Instantiate(prefab, e.Position, e.Rotation);
			if (obj == null)
			{
				DebugConsole.LogWarning($"[InstantiationsPacket] Failed to instantiate prefab '{e.PrefabName}'");
				return;
			}

			if (e.GameLayer != 0)
				obj.SetLayerRecursively(e.GameLayer);

			obj.name = e.ObjectName ?? prefab.name;

			KPrefabID id = obj.GetComponent<KPrefabID>();
			if (id != null)
			{
				if (e.InitializeId)
				{
					id.InstanceID = KPrefabID.GetUniqueID();
					KPrefabIDTracker.Get().Register(id);
				}

				id.InitializeTags(force_initialize: true);

				KPrefabID source = prefab.GetComponent<KPrefabID>();
				if (source != null)
				{
					id.CopyTags(source);
					id.CopyInitFunctions(source);
				}

				id.RunInstantiateFn();
			}

			obj.SetActive(true);
		}
	}
}
