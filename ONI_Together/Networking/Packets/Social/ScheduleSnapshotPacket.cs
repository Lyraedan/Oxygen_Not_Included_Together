using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;
using System.Collections.Generic;
using System.IO;

namespace ONI_Together.Networking.Packets.Social
{
	public sealed class ScheduleSnapshotEntry
	{
		public string Name = string.Empty;
		public bool AlarmActivated;
		public bool IsDefaultForBionics;
		public int ProgressTimetableIndex;
		public int[] Tones = System.Array.Empty<int>();
		public List<string> BlockGroupIds = new();
		public List<int> AssignedNetIds = new();

		internal void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid schedule snapshot entry");
			ScheduleSyncProtocol.WriteString(writer, Name, ScheduleSyncProtocol.MaxScheduleNameLength);
			writer.Write(AlarmActivated);
			writer.Write(IsDefaultForBionics);
			writer.Write(ProgressTimetableIndex);
			writer.Write(Tones.Length);
			foreach (int tone in Tones)
				writer.Write(tone);
			writer.Write(BlockGroupIds.Count);
			foreach (string groupId in BlockGroupIds)
				ScheduleSyncProtocol.WriteString(writer, groupId, ScheduleSyncProtocol.MaxGroupIdLength);
			writer.Write(AssignedNetIds.Count);
			foreach (int netId in AssignedNetIds)
				writer.Write(netId);
		}

		internal static ScheduleSnapshotEntry Deserialize(BinaryReader reader)
		{
			var entry = new ScheduleSnapshotEntry
			{
				Name = ScheduleSyncProtocol.ReadString(reader, ScheduleSyncProtocol.MaxScheduleNameLength),
				AlarmActivated = reader.ReadBoolean(),
				IsDefaultForBionics = reader.ReadBoolean(),
				ProgressTimetableIndex = reader.ReadInt32()
			};
			entry.Tones = ReadTones(reader);
			entry.BlockGroupIds = ReadGroupIds(reader);
			entry.AssignedNetIds = ReadAssignments(reader);
			if (!entry.IsWireValid())
				throw new InvalidDataException("Invalid schedule snapshot entry");
			return entry;
		}

		internal bool IsWireValid()
		{
			int blockCount = BlockGroupIds?.Count ?? 0;
			if (Name == null || Name.Length > ScheduleSyncProtocol.MaxScheduleNameLength ||
			    blockCount < ScheduleSyncProtocol.BlocksPerTimetable ||
			    blockCount > ScheduleSyncProtocol.MaxBlocksPerSchedule ||
			    blockCount % ScheduleSyncProtocol.BlocksPerTimetable != 0)
				return false;
			if (ProgressTimetableIndex < 0 || ProgressTimetableIndex >= blockCount / ScheduleSyncProtocol.BlocksPerTimetable)
				return false;
			if (Tones == null || Tones.Length > ScheduleSyncProtocol.MaxToneCount ||
			    AssignedNetIds == null || AssignedNetIds.Count > ScheduleSyncProtocol.MaxAssignmentsPerSchedule)
				return false;
			foreach (string groupId in BlockGroupIds)
				if (string.IsNullOrEmpty(groupId) || groupId.Length > ScheduleSyncProtocol.MaxGroupIdLength)
					return false;
			return HasUniqueAssignments();
		}

		private bool HasUniqueAssignments()
		{
			var seen = new HashSet<int>();
			foreach (int netId in AssignedNetIds)
				if (netId == 0 || !seen.Add(netId))
					return false;
			return true;
		}

		private static int[] ReadTones(BinaryReader reader)
		{
			int count = ReadCount(reader, ScheduleSyncProtocol.MaxToneCount, "tone");
			var tones = new int[count];
			for (int i = 0; i < count; i++)
				tones[i] = reader.ReadInt32();
			return tones;
		}

		private static List<string> ReadGroupIds(BinaryReader reader)
		{
			int count = ReadCount(reader, ScheduleSyncProtocol.MaxBlocksPerSchedule, "block");
			var groups = new List<string>(count);
			for (int i = 0; i < count; i++)
				groups.Add(ScheduleSyncProtocol.ReadString(reader, ScheduleSyncProtocol.MaxGroupIdLength));
			return groups;
		}

		private static List<int> ReadAssignments(BinaryReader reader)
		{
			int count = ReadCount(reader, ScheduleSyncProtocol.MaxAssignmentsPerSchedule, "assignment");
			var assignments = new List<int>(count);
			for (int i = 0; i < count; i++)
				assignments.Add(reader.ReadInt32());
			return assignments;
		}

		private static int ReadCount(BinaryReader reader, int maximum, string field)
		{
			int count = reader.ReadInt32();
			if (count < 0 || count > maximum)
				throw new InvalidDataException($"Invalid schedule {field} count: {count}");
			return count;
		}
	}

	public sealed class ScheduleSnapshotPacket : IPacket, IHostOnlyPacket
	{
		public long Revision;
		public bool HasDeletedDefaultBionicSchedule;
		public int ScheduleNameIncrementor;
		public List<ScheduleSnapshotEntry> Schedules = new();

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid schedule snapshot");
			writer.Write(Revision);
			writer.Write(HasDeletedDefaultBionicSchedule);
			writer.Write(ScheduleNameIncrementor);
			writer.Write(Schedules.Count);
			foreach (ScheduleSnapshotEntry entry in Schedules)
				entry.Serialize(writer);
		}

		public void Deserialize(BinaryReader reader)
		{
			Revision = reader.ReadInt64();
			HasDeletedDefaultBionicSchedule = reader.ReadBoolean();
			ScheduleNameIncrementor = reader.ReadInt32();
			int count = reader.ReadInt32();
			if (count <= 0 || count > ScheduleSyncProtocol.MaxSchedules)
				throw new InvalidDataException($"Invalid schedule count: {count}");
			Schedules = new List<ScheduleSnapshotEntry>(count);
			int totalBlocks = 0;
			int totalAssignments = 0;
			for (int i = 0; i < count; i++)
			{
				ScheduleSnapshotEntry entry = ScheduleSnapshotEntry.Deserialize(reader);
				totalBlocks += entry.BlockGroupIds.Count;
				totalAssignments += entry.AssignedNetIds.Count;
				if (totalBlocks > ScheduleSyncProtocol.MaxTotalBlocks ||
				    totalAssignments > ScheduleSyncProtocol.MaxTotalAssignments)
					throw new InvalidDataException("Schedule snapshot aggregate limit exceeded");
				Schedules.Add(entry);
			}
			if (!IsWireValid())
				throw new InvalidDataException("Invalid schedule revision");
		}

		public void OnDispatched()
		{
			ScheduleSyncCoordinator.ApplySnapshot(this);
		}

		internal bool IsWireValid()
		{
			if (Revision <= 0 || ScheduleNameIncrementor < 0 ||
			    Schedules == null || Schedules.Count <= 0 ||
			    Schedules.Count > ScheduleSyncProtocol.MaxSchedules)
				return false;
			int totalBlocks = 0;
			int totalAssignments = 0;
			foreach (ScheduleSnapshotEntry entry in Schedules)
			{
				if (entry == null || !entry.IsWireValid())
					return false;
				totalBlocks += entry.BlockGroupIds.Count;
				totalAssignments += entry.AssignedNetIds.Count;
			}
			return totalBlocks <= ScheduleSyncProtocol.MaxTotalBlocks &&
			       totalAssignments <= ScheduleSyncProtocol.MaxTotalAssignments &&
			       HasUniqueAssignments();
		}

		private bool HasUniqueAssignments()
		{
			var seen = new HashSet<int>();
			foreach (ScheduleSnapshotEntry entry in Schedules)
				foreach (int netId in entry.AssignedNetIds)
					if (!seen.Add(netId))
						return false;
			return true;
		}
	}
}
