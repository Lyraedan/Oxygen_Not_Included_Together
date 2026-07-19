using ONI_Together.Networking.Packets.Social;
using Shared.Interfaces.Networking;
using System.IO;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class ScheduleAuthorityTests
	{
		[UnitTest(name: "Schedule request: revision must match host", category: "Networking")]
		public static UnitTestResult RequestRevisionMustMatchHost()
		{
			if (!ScheduleSyncProtocol.IsCurrentRevision(7, 7))
				return UnitTestResult.Fail("Matching schedule revision was rejected");
			if (ScheduleSyncProtocol.IsCurrentRevision(6, 7))
				return UnitTestResult.Fail("Stale schedule revision was accepted");
			if (ScheduleSyncProtocol.IsCurrentRevision(8, 7))
				return UnitTestResult.Fail("Future schedule revision was accepted");
			if (ScheduleSyncProtocol.IsCurrentRevision(-1, 0))
				return UnitTestResult.Fail("Negative schedule revision was accepted");

			return UnitTestResult.Pass("Schedule requests require the exact host revision");
		}

		[UnitTest(name: "Schedule snapshot: only newer revisions apply", category: "Networking")]
		public static UnitTestResult SnapshotRequiresNewerRevision()
		{
			if (!ScheduleSyncProtocol.ShouldApplySnapshot(8, 7))
				return UnitTestResult.Fail("Newer schedule snapshot was rejected");
			if (ScheduleSyncProtocol.ShouldApplySnapshot(7, 7) ||
			    ScheduleSyncProtocol.ShouldApplySnapshot(6, 7) ||
			    ScheduleSyncProtocol.ShouldApplySnapshot(0, 0))
				return UnitTestResult.Fail("Duplicate, stale, or zero schedule snapshot was accepted");

			return UnitTestResult.Pass("Schedule snapshot replay ordering is monotonic");
		}

		[UnitTest(name: "Schedule snapshot: bounded absolute state round-trip", category: "Networking")]
		public static UnitTestResult SnapshotRoundTripsAbsoluteState()
		{
			var entry = new ScheduleSnapshotEntry
			{
				Name = "Night Shift",
				AlarmActivated = true,
				ProgressTimetableIndex = 0,
				Tones = new[] { 1, 2, 3, 4 },
				AssignedNetIds = new() { 10, 20 }
			};
			for (int i = 0; i < ScheduleSyncProtocol.BlocksPerTimetable; i++)
				entry.BlockGroupIds.Add(i == 23 ? "Sleep" : "Work");

			var packet = new ScheduleSnapshotPacket
			{
				Revision = 3,
				HasDeletedDefaultBionicSchedule = true,
				ScheduleNameIncrementor = 17,
				Schedules = new() { entry }
			};
			if (packet is not IHostOnlyPacket)
				return UnitTestResult.Fail("Schedule snapshot is not host-only");

			using var stream = new MemoryStream();
			packet.Serialize(new BinaryWriter(stream));
			stream.Position = 0;
			var copy = new ScheduleSnapshotPacket();
			copy.Deserialize(new BinaryReader(stream));

			if (copy.Revision != 3 || !copy.HasDeletedDefaultBionicSchedule ||
			    copy.ScheduleNameIncrementor != 17 || copy.Schedules.Count != 1)
				return UnitTestResult.Fail("Schedule snapshot envelope changed during round-trip");
			var copied = copy.Schedules[0];
			if (copied.Name != "Night Shift" || !copied.AlarmActivated || copied.Tones.Length != 4)
				return UnitTestResult.Fail("Schedule metadata changed during round-trip");
			if (copied.BlockGroupIds.Count != 24 || copied.BlockGroupIds[23] != "Sleep")
				return UnitTestResult.Fail("Schedule blocks changed during round-trip");
			if (copied.AssignedNetIds.Count != 2 || copied.AssignedNetIds[1] != 20)
				return UnitTestResult.Fail("Schedule assignments changed during round-trip");

			return UnitTestResult.Pass("Absolute schedule snapshot is bounded, host-only, and round-trips");
		}

		[UnitTest(name: "Schedule snapshot: oversized schedule count rejected", category: "Networking")]
		public static UnitTestResult SnapshotRejectsOversizedScheduleCount()
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
			{
				writer.Write(1L);
				writer.Write(false);
				writer.Write(0);
				writer.Write(ScheduleSyncProtocol.MaxSchedules + 1);
			}
			stream.Position = 0;
			try
			{
				new ScheduleSnapshotPacket().Deserialize(new BinaryReader(stream));
				return UnitTestResult.Fail("Oversized schedule count was accepted");
			}
			catch (InvalidDataException)
			{
				return UnitTestResult.Pass("Oversized schedule count is rejected before allocation");
			}
		}

		[UnitTest(name: "Schedule block request: wire bounds enforced", category: "Networking")]
		public static UnitTestResult BlockRequestEnforcesWireBounds()
		{
			var valid = new ScheduleBlockUpdatePacket
			{
				ClientRequestId = 1,
				BaseRevision = 4,
				ScheduleIndex = 2,
				BlockIndex = 23,
				GroupId = "Work"
			};
			if (!valid.IsWireValid())
				return UnitTestResult.Fail("Valid schedule block request was rejected");

			valid.ScheduleIndex = ScheduleSyncProtocol.MaxSchedules;
			if (valid.IsWireValid())
				return UnitTestResult.Fail("Out-of-range schedule index was accepted");
			valid.ScheduleIndex = 0;
			valid.BlockIndex = ScheduleSyncProtocol.MaxBlocksPerSchedule;
			if (valid.IsWireValid())
				return UnitTestResult.Fail("Out-of-range block index was accepted");
			valid.BlockIndex = 0;
			valid.GroupId = new string('x', ScheduleSyncProtocol.MaxGroupIdLength + 1);
			if (valid.IsWireValid())
				return UnitTestResult.Fail("Oversized schedule group id was accepted");

			return UnitTestResult.Pass("Schedule block requests enforce revision, index, and string bounds");
		}

		[UnitTest(name: "Schedule requests: all mutation shapes are bounded", category: "Networking")]
		public static UnitTestResult AllMutationShapesAreBounded()
		{
			var add = new ScheduleAddPacket { ClientRequestId = 1, BaseRevision = 0 };
			var duplicate = new ScheduleAddPacket
			{
				ClientRequestId = 2,
				BaseRevision = 0,
				Duplicated = true,
				SourceScheduleIndex = 0
			};
			var delete = new ScheduleDeletePacket { ClientRequestId = 3, BaseRevision = 0, ScheduleIndex = 1 };
			var details = new ScheduleDetailsUpdatePacket
			{
				ClientRequestId = 4,
				BaseRevision = 0,
				ScheduleIndex = 0,
				UpdateType = ScheduleDetailsUpdatePacket.DetailsUpdateType.NAME,
				Name = "Night"
			};
			var row = new ScheduleRowPacket
			{
				ClientRequestId = 5,
				BaseRevision = 0,
				ScheduleIndex = 0,
				Action = ScheduleRowPacket.RowAction.ROTATE_LEFT,
				TimetableToIndex = 0
			};
			var assignment = new ScheduleAssignmentPacket
			{
				ClientRequestId = 6,
				BaseRevision = 0,
				NetId = -42,
				ScheduleIndex = 0
			};

			if (!add.IsWireValid() || !duplicate.IsWireValid() || !delete.IsWireValid() ||
			    !details.IsWireValid() || !row.IsWireValid() || !assignment.IsWireValid())
				return UnitTestResult.Fail("A valid schedule mutation shape was rejected");

			row.Action = (ScheduleRowPacket.RowAction)255;
			details.UpdateType = (ScheduleDetailsUpdatePacket.DetailsUpdateType)255;
			assignment.BaseRevision = -1;
			if (row.IsWireValid() || details.IsWireValid() || assignment.IsWireValid())
				return UnitTestResult.Fail("An invalid enum or revision was accepted");

			return UnitTestResult.Pass("All schedule mutation request shapes enforce structural bounds");
		}

		[UnitTest(name: "Schedule request: reset and acknowledgement are bounded", category: "Networking")]
		public static UnitTestResult ResetAndAcknowledgementAreBounded()
		{
			var reset = new ScheduleRowPacket
			{
				ClientRequestId = 7,
				BaseRevision = 2,
				ScheduleIndex = 0,
				Action = ScheduleRowPacket.RowAction.RESET_DEFAULT,
				TimetableToIndex = 0
			};
			var acknowledgement = new ScheduleRequestAckPacket
			{
				ClientId = 11,
				ClientRequestId = 7,
				HostRevision = 3,
				Accepted = true
			};

			if (!reset.IsWireValid())
				return UnitTestResult.Fail("Valid schedule reset request was rejected");
			if (acknowledgement is not IHostOnlyPacket || !acknowledgement.IsWireValid())
				return UnitTestResult.Fail("Schedule acknowledgement is not bounded and host-only");

			using var stream = new MemoryStream();
			acknowledgement.Serialize(new BinaryWriter(stream));
			stream.Position = 0;
			var copy = new ScheduleRequestAckPacket();
			copy.Deserialize(new BinaryReader(stream));
			if (copy.ClientId != 11 || copy.ClientRequestId != 7 || copy.HostRevision != 3 || !copy.Accepted)
				return UnitTestResult.Fail("Schedule acknowledgement changed during round-trip");

			acknowledgement.ClientRequestId = 0;
			if (acknowledgement.IsWireValid())
				return UnitTestResult.Fail("Zero schedule request id was accepted");

			return UnitTestResult.Pass("Schedule reset and acknowledgement shapes are bounded");
		}

		[UnitTest(name: "Schedule queue: delete rebases pending indexes", category: "Networking")]
		public static UnitTestResult DeleteRebasesPendingIndexes()
		{
			if (!ScheduleSyncProtocol.TryRebaseScheduleIndex(1, 2, out int before) || before != 1)
				return UnitTestResult.Fail("Index before deleted schedule was changed");
			if (ScheduleSyncProtocol.TryRebaseScheduleIndex(2, 2, out _))
				return UnitTestResult.Fail("Request targeting deleted schedule was retained");
			if (!ScheduleSyncProtocol.TryRebaseScheduleIndex(4, 2, out int after) || after != 3)
				return UnitTestResult.Fail("Index after deleted schedule was not shifted");

			return UnitTestResult.Pass("Pending schedule requests preserve target identity across deletes");
		}
	}
}
