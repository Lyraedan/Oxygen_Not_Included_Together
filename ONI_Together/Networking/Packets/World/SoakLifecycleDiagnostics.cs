#if DEBUG
using System.IO;

namespace ONI_Together.Networking.Packets.World
{
	public sealed class SoakLifecycleDiagnostics
	{
		private const int MaxCount = 4 * 1024 * 1024;

		public int MissingLiveCount;
		public int UnexpectedLiveCount;
		public int TombstonedLiveCount;
		public int UnassignedLiveCount;

		internal bool IsValid => MissingLiveCount == 0 && UnexpectedLiveCount == 0
		                         && TombstonedLiveCount == 0 && UnassignedLiveCount == 0;

		internal void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(MissingLiveCount);
			writer.Write(UnexpectedLiveCount);
			writer.Write(TombstonedLiveCount);
			writer.Write(UnassignedLiveCount);
		}

		internal static SoakLifecycleDiagnostics Deserialize(BinaryReader reader)
		{
			var diagnostics = new SoakLifecycleDiagnostics
			{
				MissingLiveCount = reader.ReadInt32(),
				UnexpectedLiveCount = reader.ReadInt32(),
				TombstonedLiveCount = reader.ReadInt32(),
				UnassignedLiveCount = reader.ReadInt32(),
			};
			diagnostics.Validate();
			return diagnostics;
		}

		internal bool Matches(SoakLifecycleDiagnostics other)
		{
			return other != null && MissingLiveCount == other.MissingLiveCount
			       && UnexpectedLiveCount == other.UnexpectedLiveCount
			       && TombstonedLiveCount == other.TombstonedLiveCount
			       && UnassignedLiveCount == other.UnassignedLiveCount;
		}

		internal string ToLogFields()
		{
			return $"missing={MissingLiveCount} unexpected={UnexpectedLiveCount} " +
			       $"tombstoned={TombstonedLiveCount} unassigned={UnassignedLiveCount}";
		}

		internal void Validate()
		{
			if (!ValidCount(MissingLiveCount) || !ValidCount(UnexpectedLiveCount)
			    || !ValidCount(TombstonedLiveCount) || !ValidCount(UnassignedLiveCount))
				throw new InvalidDataException("Invalid soak lifecycle diagnostics");
		}

		private static bool ValidCount(int value) => value >= 0 && value <= MaxCount;
	}
}
#endif
