using Shared.Profiling;
using System.Collections.Generic;
using UnityEngine;

namespace ONI_Together.Networking.OxySync
{
	public static class WorldChunkHelper
	{
		public static int ChunkSize { get; set; } = 16;
		private const int MAX_CHUNKS_AXIS = 100;
		private const int GROUP_BASE = 10000;

		public static (int cx, int cy) CellToChunk(int cell)
		{
			Grid.CellToXY(cell, out int x, out int y);
			return (x / ChunkSize, y / ChunkSize);
		}

		public static (int cx, int cy) PosToChunk(Vector3 pos)
		{
			Grid.PosToXY(pos, out int x, out int y);
			return (x / ChunkSize, y / ChunkSize);
		}

		public static int GetGroupId(int worldId, int cx, int cy)
		{
			return (worldId + 1) * GROUP_BASE + cy * MAX_CHUNKS_AXIS + cx;
		}

		public static int GetGroupId(int worldId, int cell)
		{
			var (cx, cy) = CellToChunk(cell);
			return GetGroupId(worldId, cx, cy);
		}

		public static int GetGroupId(int worldId, Vector3 pos)
		{
			Grid.PosToXY(pos, out int x, out int y);
			return GetGroupId(worldId, (x / ChunkSize), (y / ChunkSize));
		}

		public static IEnumerable<int> GetChunkGroupIdsInRect(int worldId, int xMin, int yMin, int xMax, int yMax)
		{
			int scx = xMin / ChunkSize;
			int scy = yMin / ChunkSize;
			int ecx = (xMax - 1) / ChunkSize;
			int ecy = (yMax - 1) / ChunkSize;
			for (int cy = scy; cy <= ecy; cy++)
				for (int cx = scx; cx <= ecx; cx++)
					yield return GetGroupId(worldId, cx, cy);
		}
	}
}
