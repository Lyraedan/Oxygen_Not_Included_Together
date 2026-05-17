using Shared.Profiling;
using System.IO;

namespace ONI_Together.Networking.Packets.Chores
{
	public struct ErrandEntry
	{
		public string ChoreTypeId;
		public int TargetCell;
		public string TargetLabel;
		public int PriorityClass;
		public int Priority;
		public int PersonalPriority;
		public bool IsCurrent;
		public int MoreAmount;
		public string IconSpriteName;
		public int ListIndex;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			writer.Write(ChoreTypeId ?? string.Empty);
			writer.Write(TargetCell);
			writer.Write(TargetLabel ?? string.Empty);
			writer.Write(PriorityClass);
			writer.Write(Priority);
			writer.Write(PersonalPriority);
			writer.Write(IsCurrent);
			writer.Write(MoreAmount);
			writer.Write(IconSpriteName ?? string.Empty);
			writer.Write(ListIndex);
		}

		public static ErrandEntry Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();
			return new ErrandEntry
			{
				ChoreTypeId = reader.ReadString(),
				TargetCell = reader.ReadInt32(),
				TargetLabel = reader.ReadString(),
				PriorityClass = reader.ReadInt32(),
				Priority = reader.ReadInt32(),
				PersonalPriority = reader.ReadInt32(),
				IsCurrent = reader.ReadBoolean(),
				MoreAmount = reader.ReadInt32(),
				IconSpriteName = reader.ReadString(),
				ListIndex = reader.ReadInt32()
			};
		}
	}
}
