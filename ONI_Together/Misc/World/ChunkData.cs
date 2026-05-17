using System.IO;
using Shared.Profiling;

namespace ONI_Together.Misc.World
{
	public class ChunkData
	{
		public int TileX, TileY, Width, Height;
		public ushort[] Tiles;
		public float[] Temperatures, Masses;
		public byte[] DiseaseIdx;
		public int[] DiseaseCount;

		public void Serialize(BinaryWriter w)
		{
			using var _ = Profiler.Scope();

			w.Write(TileX); w.Write(TileY);
			w.Write(Width); w.Write(Height);
			int len = Width * Height;
			w.Write(len);
			for (int i = 0; i < len; i++)
			{
				w.Write(Tiles[i]);
				w.Write(Temperatures[i]);
				w.Write(Masses[i]);
				w.Write(DiseaseIdx[i]);
				w.Write(DiseaseCount[i]);
			}
		}

		public void Deserialize(BinaryReader r)
		{
			using var _ = Profiler.Scope();

			TileX = r.ReadInt32(); TileY = r.ReadInt32();
			Width = r.ReadInt32(); Height = r.ReadInt32();
			int len = r.ReadInt32();
			Tiles = new ushort[len];
			Temperatures = new float[len];
			Masses = new float[len];
			DiseaseIdx = new byte[len];
			DiseaseCount = new int[len];
			for (int i = 0; i < len; i++)
			{
				Tiles[i] = r.ReadUInt16();
				Temperatures[i] = r.ReadSingle();
				Masses[i] = r.ReadSingle();
				DiseaseIdx[i] = r.ReadByte();
				DiseaseCount[i] = r.ReadInt32();
			}
		}

		public void Apply()
		{
			using var _ = Profiler.Scope();

			// Minimum simulation temperature - cells with mass must have temperature above this
			const float SIM_MIN_TEMPERATURE = 1f; // 1 Kelvin

			int len = Width * Height;
			for (int i = 0; i < Width; i++)
				for (int j = 0; j < Height; j++)
				{
					int idx = i + j * Width;
					int x = TileX + i, y = TileY + j;
					int cell = Grid.XYToCell(x, y);
					if (!Grid.IsValidCell(cell)) continue;

					float temperature = Temperatures[idx];
					float mass = Masses[idx];

					// Validation: The sim requires that if mass > 0, temperature must be > SIM_MIN_TEMPERATURE
					if (mass > 0f)
					{
						if (temperature <= SIM_MIN_TEMPERATURE || float.IsNaN(temperature) || float.IsInfinity(temperature))
						{
							temperature = 293.15f; // Default to room temperature
						}
					}
					else
					{
						temperature = 0f;
					}

					// Skip invalid mass data
					if (mass < 0f || float.IsNaN(mass) || float.IsInfinity(mass))
					{
						continue;
					}

					SimMessages.ModifyCell(
							cell,
							Tiles[idx],
							temperature,
							mass,
							DiseaseIdx[idx],
							DiseaseCount[idx],
							SimMessages.ReplaceType.Replace
					);
				}
		}
	}


}
