using ONI_Together.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World
{
	public class WorldUpdatePacket : IPacket
	{
		public List<CellUpdate> Updates = new List<CellUpdate>();

		public struct CellUpdate
		{
			public int Cell;
			public ushort ElementIdx;
			public float Temperature, Mass;
			public byte DiseaseIdx;
			public int DiseaseCount;
		}

		public void Serialize(BinaryWriter w)
		{
			using var _ = Profiler.Scope();

			using (var ms = new MemoryStream())
			{
				using (var deflate = new DeflateStream(ms, CompressionLevel.Fastest, true))
				using (var compressedWriter = new BinaryWriter(deflate))
				{
					compressedWriter.Write(Updates.Count);
					foreach (var u in Updates)
					{
						compressedWriter.Write(u.Cell);
						compressedWriter.Write(u.ElementIdx);
						compressedWriter.Write(u.Temperature);
						compressedWriter.Write(u.Mass);
						compressedWriter.Write(u.DiseaseIdx);
						compressedWriter.Write(u.DiseaseCount);
					}
				}

				byte[] compressedData = ms.ToArray();
				w.Write(compressedData.Length); // Write compressed length
				w.Write(compressedData);        // Write compressed payload
			}
		}

		public void Deserialize(BinaryReader r)
		{
			using var _ = Profiler.Scope();

			int compressedLength = r.ReadInt32();
			byte[] compressedData = r.ReadBytes(compressedLength);

			using (var ms = new MemoryStream(compressedData))
			using (var deflate = new DeflateStream(ms, CompressionMode.Decompress))
			using (var reader = new BinaryReader(deflate))
			{
				int count = reader.ReadInt32();
				Updates = new List<CellUpdate>(count);
				for (int i = 0; i < count; i++)
				{
					Updates.Add(new CellUpdate
					{
						Cell = reader.ReadInt32(),
						ElementIdx = reader.ReadUInt16(),
						Temperature = reader.ReadSingle(),
						Mass = reader.ReadSingle(),
						DiseaseIdx = reader.ReadByte(),
						DiseaseCount = reader.ReadInt32()
					});
				}
			}
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost) return;

			// Minimum simulation temperature - cells with mass must have temperature above this
			const float SIM_MIN_TEMPERATURE = 1f; // 1 Kelvin

			foreach (var u in Updates)
			{
				// Skip invalid cells
				if (!Grid.IsValidCell(u.Cell)) continue;

				float temperature = u.Temperature;
				float mass = u.Mass;

				// Validation: The sim requires that if mass > 0, temperature must be > SIM_MIN_TEMPERATURE
				// If we have invalid data, we need to fix it or skip the update
				if (mass > 0f)
				{
					// Ensure temperature is valid for non-vacuum cells
					if (temperature <= SIM_MIN_TEMPERATURE || float.IsNaN(temperature) || float.IsInfinity(temperature))
					{
						// Use a safe default temperature (room temperature ~293K / 20C)
						temperature = 293.15f;
					}
				}
				else
				{
					// For vacuum cells (mass == 0), temperature doesn't matter but set to 0 for consistency
					temperature = 0f;
				}

				// Skip if mass is negative (corrupt data)
				if (mass < 0f || float.IsNaN(mass) || float.IsInfinity(mass))
				{
					continue;
				}

				SimMessages.ModifyCell(
						u.Cell, u.ElementIdx,
						temperature, mass,
						u.DiseaseIdx, u.DiseaseCount,
						SimMessages.ReplaceType.Replace
				);
			}
		}
	}
}
