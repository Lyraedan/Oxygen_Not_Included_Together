using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.Navigation;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Shared.Profiling;
using UnityEngine;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace ONI_Together.Networking.Packets.Core
{
	public class NavigatorPathPacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
	{
		internal const int MaxCompressedBytes = 1024 * 1024;
		internal const int MaxDecompressedBytes = 64 * 1024;
		internal const int MaxStepCount = 8192;
		public int NetId;

		public struct PathStep
		{
			public int Cell;
			public NavType NavType;
			public byte TransitionId;

			public void Serialize(BinaryWriter writer)
			{
				using var _ = Profiler.Scope();

				writer.Write(Cell);
				writer.Write((byte)NavType);
				writer.Write(TransitionId);
			}

			public static PathStep Deserialize(BinaryReader reader)
			{
				using var _ = Profiler.Scope();

				return new PathStep
				{
					Cell = reader.ReadInt32(),
					NavType = (NavType)reader.ReadByte(),
					TransitionId = reader.ReadByte()
				};
			}
		}

		public List<PathStep> Steps = new List<PathStep>();

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			if (Steps.Count == 0 || Steps.Count > MaxStepCount)
				throw new InvalidDataException($"Invalid navigator path step count: {Steps.Count}");

			using (var memStream = new MemoryStream())
			{
				using (var tempWriter = new BinaryWriter(memStream, System.Text.Encoding.Default, leaveOpen: true))
				{
					tempWriter.Write(NetId);
					tempWriter.Write(Steps.Count);
					foreach (var step in Steps)
						step.Serialize(tempWriter);
				}

				byte[] rawData = memStream.ToArray();

				using (var compressed = new MemoryStream())
				{
					using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
					{
						gzip.Write(rawData, 0, rawData.Length);
					}

					byte[] compressedBytes = compressed.ToArray();
					writer.Write(compressedBytes.Length);
					writer.Write(compressedBytes);
				}
			}
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			int compressedLength = reader.ReadInt32();
			if (compressedLength <= 0 || compressedLength > MaxCompressedBytes)
				throw new InvalidDataException($"Invalid navigator path payload length: {compressedLength}");
			byte[] compressedBytes = reader.ReadBytes(compressedLength);
			if (compressedBytes.Length != compressedLength)
				throw new EndOfStreamException("Navigator path payload is truncated");

			using (var decompressed = new MemoryStream(Decompress(compressedBytes)))
			{
				using (var tempReader = new BinaryReader(decompressed))
				{
					NetId = tempReader.ReadInt32();
					int count = tempReader.ReadInt32();
					if (count <= 0 || count > MaxStepCount)
						throw new InvalidDataException($"Invalid navigator path step count: {count}");

					Steps.Clear();
					for (int i = 0; i < count; i++)
						Steps.Add(PathStep.Deserialize(tempReader));
					if (tempReader.BaseStream.Position != tempReader.BaseStream.Length)
						throw new InvalidDataException("Navigator path payload contains trailing bytes");
				}
			}
		}

		private static byte[] Decompress(byte[] compressedBytes)
		{
			using var input = new MemoryStream(compressedBytes);
			using var gzip = new GZipStream(input, CompressionMode.Decompress);
			using var output = new MemoryStream();
			var buffer = new byte[8192];
			int total = 0;
			while (true)
			{
				int remaining = MaxDecompressedBytes - total;
				int read = gzip.Read(buffer, 0, remaining < buffer.Length ? remaining + 1 : buffer.Length);
				if (read == 0)
					return output.ToArray();
				total += read;
				if (total > MaxDecompressedBytes)
					throw new InvalidDataException($"Navigator path expands beyond {MaxDecompressedBytes} bytes");
				output.Write(buffer, 0, read);
			}
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
				return;

			if (!PassesPreliminaryChecks(out var entity, out var navigator))
				return;

			var newPath = new PathFinder.Path
			{
				nodes = new List<PathFinder.Path.Node>(Steps.Count)
			};

			foreach (var step in Steps)
			{
				newPath.nodes.Add(new PathFinder.Path.Node
				{
					cell = step.Cell,
					navType = step.NavType,
					transitionId = step.TransitionId
				});
			}

			navigator.path = newPath;

			// Final destination position
			int finalCell = Steps[Steps.Count - 1].Cell;
			Vector3 finalPos = Grid.CellToPosCBC(finalCell, Grid.SceneLayer.Move);

			// Create a dummy GameObject
			GameObject dummyTarget = new GameObject($"NetNav_Target_{NetId}");
			dummyTarget.transform.position = finalPos;
			dummyTarget.transform.SetParent(Game.Instance.transform, worldPositionStays: true);

			var targetBehaviour = dummyTarget.AddComponent<KMonoBehaviour>();

			System.Action cleanup = () =>
			{
				if (dummyTarget != null)
				{
					Object.Destroy(dummyTarget);
					DebugConsole.Log($"[NavigatorPathPacket] Cleaned up dummy target for NetId {NetId}");
					navigator.SetCanAdvance(false);
				}
			};

			// Inject callback into navigator events
			navigator.Subscribe((int)GameHashes.DestinationReached, (data) => cleanup.Invoke());
			navigator.Subscribe((int)GameHashes.NavigationFailed, (data) => cleanup.Invoke());

			// Trigger movement
			bool result = navigator.ClientGoTo(targetBehaviour, new CellOffset[] { CellOffset.none });

			if (!result)
			{
				DebugConsole.LogWarning($"[NavigatorPathPacket] ClientGoTo failed for {NetId}");
				Object.Destroy(dummyTarget); // immediate fallback cleanup
			}

			DebugConsole.Log($"[NavigatorPathPacket] Path with {Steps.Count} nodes applied to NetId {NetId}");
		}

		private bool PassesPreliminaryChecks(out Component entity, out Navigator navigator)
		{
			using var _ = Profiler.Scope();

			entity = null;
			navigator = null;

			if (!NetworkIdentityRegistry.TryGet(NetId, out var foundEntity))
			{
				DebugConsole.LogWarning($"[NavigatorPathPacket] Could not find entity with NetId {NetId}");
				return false;
			}

			if (!foundEntity)
				return false;

			entity = foundEntity;

			if (!entity.TryGetComponent(out navigator))
			{
				DebugConsole.LogWarning($"[NavigatorPathPacket] Entity {NetId} has no Navigator");
				return false;
			}

			if (!navigator)
				return false;

			if (Steps == null/* || Steps.Count < 2*/)
			{
				DebugConsole.LogWarning($"[NavigatorPathPacket] Received invalid path for {NetId}");
				return false;
			}

			return true;
		}

	}
}
