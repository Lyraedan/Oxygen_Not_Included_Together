using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.World;
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
			TryApplyAndCaptureTargets(out _);
		}

		internal bool TryApplyAndCaptureTargets(out List<SnapshotGridCell> targets)
		{
			using var _ = Profiler.Scope();
			targets = new List<SnapshotGridCell>();
			if (!HasCompleteCellData())
				return false;

			for (int i = 0; i < Width; i++)
				for (int j = 0; j < Height; j++)
				{
					int idx = i + j * Width;
					int x = TileX + i, y = TileY + j;
					if (x < 0 || x >= Grid.WidthInCells || y < 0 || y >= Grid.HeightInCells)
						continue;
					int cell = Grid.XYToCell(x, y);
					if (!Grid.IsValidCell(cell)) continue;

					var update = new WorldUpdatePacket.CellUpdate
					{
						Temperature = Temperatures[idx],
						Mass = Masses[idx],
						ReplaceType = SimMessages.ReplaceType.Replace
					};
					if (!WorldUpdatePacket.TryGetApplyValues(update, out float temperature, out float mass))
						return false;

					SimMessages.ModifyCell(
							cell,
							Tiles[idx],
							temperature,
							mass,
							DiseaseIdx[idx],
							DiseaseCount[idx],
							update.ReplaceType
					);
					targets.Add(new SnapshotGridCell
					{
						Cell = cell,
						ElementIdx = Tiles[idx],
						Temperature = temperature,
						Mass = mass,
						DiseaseIdx = DiseaseIdx[idx],
						DiseaseCount = DiseaseCount[idx],
					}.Normalized());
				}
			return true;
		}

		private bool HasCompleteCellData()
		{
			long count = (long)Width * Height;
			return count >= 0 && count <= int.MaxValue
			       && Tiles?.Length == count && Temperatures?.Length == count
			       && Masses?.Length == count && DiseaseIdx?.Length == count
			       && DiseaseCount?.Length == count;
		}
	}


}
