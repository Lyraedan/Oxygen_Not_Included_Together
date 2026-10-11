using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.Tools.Deconstruct
{
	public class DeconstructCompletePacket : IPacket
	{
		public int Cell, ObjectLayer;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(Cell);
			writer.Write(ObjectLayer);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			Cell = reader.ReadInt32();
			ObjectLayer = reader.ReadInt32();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (!Grid.IsValidCell(Cell))
				return;

			GameObject go = Grid.Objects[Cell, ObjectLayer];
			if (go == null)
				return;

			if (go.TryGetComponent<Deconstructable>(out var deconstructable) && !deconstructable.HasBeenDestroyed)
			{
				DebugConsole.Log($"[DeconstructCompletePacket] Removing deconstructed object at cell {Cell} on objectlayer {ObjectLayer} on client.");
				// Material drops are host authoritative. Running the full deconstruction
				// path again can access storage after cleanup (the coat rack crash).
				Util.KDestroyGameObject(go);
			}
		}
	}
}
