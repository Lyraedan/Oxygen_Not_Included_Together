using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.World
{
	internal class WorkProgressPacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
	{
		public WorkProgressPacket() { }

		public WorkProgressPacket(int cell, float workTimeRemaining)
		{
			Cell = cell;
			WorkTimeRemaining = workTimeRemaining;
		}

		int Cell;
		float WorkTimeRemaining;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(Cell);
			writer.Write(WorkTimeRemaining);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			Cell = reader.ReadInt32();
			WorkTimeRemaining = reader.ReadSingle();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
				return;

			if (!Grid.IsValidCell(Cell))
				return;

			for (int layer = 0; layer < (int)Grid.SceneLayer.SceneMAX; layer++)
			{
				GameObject obj = Grid.Objects[Cell, layer];
				if (obj == null)
					continue;

				Workable workable = obj.GetComponent<Constructable>();
				if (workable == null)
					workable = obj.GetComponent<Deconstructable>();
				if (workable == null)
					continue;

				workable.WorkTimeRemaining = WorkTimeRemaining;
				if (WorkTimeRemaining <= 0f)
					workable.ShowProgressBar(false);
				else
					workable.ShowProgressBar(true);
				return;
			}
		}
	}
}
