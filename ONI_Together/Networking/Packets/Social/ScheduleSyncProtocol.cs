using System.IO;
using System.Text;

namespace ONI_Together.Networking.Packets.Social
	{
	internal static class ScheduleSyncProtocol
	{
		private static readonly UTF8Encoding StrictUtf8 = new(false, true);
		internal const int BlocksPerTimetable = 24;
		internal const int MaxSchedules = 128;
		internal const int MaxBlocksPerSchedule = BlocksPerTimetable * 64;
		internal const int MaxTotalBlocks = 8192;
		internal const int MaxAssignmentsPerSchedule = 1024;
		internal const int MaxTotalAssignments = 4096;
		internal const int MaxScheduleNameLength = 256;
		internal const int MaxGroupIdLength = 128;
		internal const int MaxToneCount = 16;

		internal static bool IsCurrentRevision(long requestRevision, long hostRevision)
			=> requestRevision >= 0 && requestRevision == hostRevision;

		internal static bool ShouldApplySnapshot(long incomingRevision, long appliedRevision)
			=> incomingRevision > 0 && incomingRevision > appliedRevision;

		internal static bool TryRebaseScheduleIndex(
			int currentIndex,
			int deletedIndex,
			out int rebasedIndex)
		{
			rebasedIndex = currentIndex;
			if (currentIndex < 0 || deletedIndex < 0 || currentIndex == deletedIndex)
				return false;
			if (currentIndex > deletedIndex)
				rebasedIndex--;
			return true;
		}

		internal static void WriteString(BinaryWriter writer, string value, int maxCharacters)
		{
			value ??= string.Empty;
			if (value.Length > maxCharacters)
				throw new InvalidDataException("Schedule string exceeds character limit");
			byte[] bytes = StrictUtf8.GetBytes(value);
			if (bytes.Length > maxCharacters * 4)
				throw new InvalidDataException("Schedule string exceeds byte limit");
			writer.Write(bytes.Length);
			writer.Write(bytes);
		}

		internal static string ReadString(BinaryReader reader, int maxCharacters)
		{
			int byteCount = reader.ReadInt32();
			if (byteCount < 0 || byteCount > maxCharacters * 4)
				throw new InvalidDataException("Invalid schedule string byte length");
			byte[] bytes = reader.ReadBytes(byteCount);
			if (bytes.Length != byteCount)
				throw new EndOfStreamException("Truncated schedule string");
			string value = StrictUtf8.GetString(bytes);
			if (value.Length > maxCharacters)
				throw new InvalidDataException("Schedule string exceeds character limit");
			return value;
		}
	}
}
