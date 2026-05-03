using ONI_MP.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;
using ONI_MP.Networking.Components;

namespace ONI_MP.Networking.Packets.World
{
	public class StructureStatePacket : IPacket
	{
		public int Cell;
		public float Value; // Joules for Battery, Progress for others
		public bool IsActive; // Operational active state
		public StructureStateSyncer.StructureType StructureType;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(Cell);
			writer.Write(Value);
			writer.Write(IsActive);
			writer.Write((int)StructureType);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			Cell = reader.ReadInt32();
			Value = reader.ReadSingle();
			IsActive = reader.ReadBoolean();
			StructureType = (StructureStateSyncer.StructureType)reader.ReadInt32();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost) return;

			// Handled by StructureStateSyncer on client
			StructureStateSyncer.HandlePacket(this);
		}
	}
}
