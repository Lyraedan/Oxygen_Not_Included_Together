using System.IO;
using Shared.Profiling;

namespace ONI_Together.Misc.World
{
	public class WorldSave
	{
		public byte[] Data { get; set; }
		public string Name { get; set; }
		public long SnapshotGeneration { get; set; }

		public WorldSave(string name, byte[] data, long snapshotGeneration = 0)
		{
			using var _ = Profiler.Scope();

			Name = name;
			Data = data;
			SnapshotGeneration = snapshotGeneration;
		}

		public static WorldSave FromFile(string filePath)
		{
			using var _ = Profiler.Scope();

			if (!File.Exists(filePath))
				throw new FileNotFoundException($"Save file not found: {filePath}");

			string name = Path.GetFileName(filePath);
			byte[] data = File.ReadAllBytes(filePath);
			return new WorldSave(name, data);
		}
	}
}
