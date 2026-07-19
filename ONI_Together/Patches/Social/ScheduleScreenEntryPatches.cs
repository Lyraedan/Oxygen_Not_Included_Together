using HarmonyLib;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Social;
using Shared.Profiling;
using System;
using UnityEngine;

namespace ONI_Together.Patches.Social
{
	internal static class ScheduleScreenEntryPatches
	{
		[HarmonyPatch(typeof(ScheduleScreenEntry), nameof(ScheduleScreenEntry.DuplicateSchedule))]
		public static class DuplicateSchedulePatch
		{
			public static bool Prefix(ScheduleScreenEntry __instance)
			{
				using var _ = Profiler.Scope();
				if (!MultiplayerSession.InSession ||
				    ScheduleSyncCoordinator.IsApplyingAuthoritativeMutation)
					return true;
				if (!TryGetSchedule(__instance, out Schedule schedule, out int index) ||
				    !ScheduleSyncCoordinator.CanDuplicateSchedule(schedule))
					return false;
				if (MultiplayerSession.IsHost)
					return true;
				ScheduleSyncCoordinator.SendClientRequest(new ScheduleAddPacket
				{
					Duplicated = true,
					SourceScheduleIndex = index
				});
				return false;
			}
		}

		[HarmonyPatch(typeof(ScheduleScreenEntry), nameof(ScheduleScreenEntry.DeleteSchedule))]
		public static class DeleteSchedulePatch
		{
			public static bool Prefix(ScheduleScreenEntry __instance)
				=> RequestDeleteOrRun(__instance);
		}

		[HarmonyPatch(typeof(ScheduleScreenEntry), nameof(ScheduleScreenEntry.OnDeleteClicked))]
		public static class OnDeleteClickedPatch
		{
			public static bool Prefix(ScheduleScreenEntry __instance)
				=> RequestDeleteOrRun(__instance);
		}

		[HarmonyPatch(typeof(ScheduleScreenEntry), nameof(ScheduleScreenEntry.DuplicateTimetableRow))]
		public static class DuplicateTimetableRowPatch
		{
			public static bool Prefix(ScheduleScreenEntry __instance, int sourceTimetableIdx)
			{
				using var _ = Profiler.Scope();
				if (!MultiplayerSession.InSession ||
				    ScheduleSyncCoordinator.IsApplyingAuthoritativeMutation)
					return true;
				if (!TryGetSchedule(__instance, out Schedule schedule, out int index) ||
				    sourceTimetableIdx < 0 ||
				    sourceTimetableIdx >= schedule.blocks.Count / ScheduleSyncProtocol.BlocksPerTimetable ||
				    !ScheduleSyncCoordinator.CanDuplicateRow(schedule))
					return false;
				if (MultiplayerSession.IsHost)
					return true;
				ScheduleSyncCoordinator.SendClientRequest(new ScheduleRowPacket
				{
					ScheduleIndex = index,
					Action = ScheduleRowPacket.RowAction.DUPLICATE,
					TimetableToIndex = sourceTimetableIdx
				});
				return false;
			}

			public static void Postfix()
			{
				ScheduleSyncCoordinator.PublishHostMutation();
			}
		}

		[HarmonyPatch(typeof(ScheduleScreenEntry), nameof(ScheduleScreenEntry.RemoveTimetableRow))]
		public static class RemoveTimetableRowPatch
		{
			public static bool Prefix(ScheduleScreenEntry __instance, GameObject row, ref int __state)
			{
				using var _ = Profiler.Scope();
				__state = __instance?.timetableRows?.IndexOf(row) ?? -1;
				if (!MultiplayerSession.InSession || MultiplayerSession.IsHost ||
				    ScheduleSyncCoordinator.IsApplyingAuthoritativeMutation)
					return true;
				if (!TryGetSchedule(__instance, out Schedule schedule, out int index) ||
				    __state < 0 || schedule.blocks.Count <= ScheduleSyncProtocol.BlocksPerTimetable)
					return false;
				ScheduleSyncCoordinator.SendClientRequest(new ScheduleRowPacket
				{
					ScheduleIndex = index,
					Action = ScheduleRowPacket.RowAction.DELETE,
					TimetableToIndex = __state
				});
				return false;
			}

			public static void Postfix(int __state)
			{
				if (__state >= 0)
					ScheduleSyncCoordinator.PublishHostMutation();
			}
		}

		[HarmonyPatch(typeof(ScheduleScreenEntry), nameof(ScheduleScreenEntry.OnNameChanged))]
		public static class OnNameChangedPatch
		{
			public static bool Prefix(ScheduleScreenEntry __instance, string newName)
			{
				using var _ = Profiler.Scope();
				if (!MultiplayerSession.InSession)
					return true;
				if (!ScheduleSyncCoordinator.IsValidName(newName))
					return false;
				if (MultiplayerSession.IsHost || ScheduleSyncCoordinator.IsApplyingAuthoritativeMutation)
					return true;
				if (!TryGetSchedule(__instance, out Schedule unused, out int index))
					return false;
				ScheduleSyncCoordinator.SendClientRequest(new ScheduleDetailsUpdatePacket
				{
					ScheduleIndex = index,
					Name = newName,
					UpdateType = ScheduleDetailsUpdatePacket.DetailsUpdateType.NAME
				});
				return false;
			}

			public static void Postfix()
			{
				ScheduleSyncCoordinator.PublishHostMutation();
			}
		}

		[HarmonyPatch(typeof(ScheduleScreenEntry), nameof(ScheduleScreenEntry.OnAlarmClicked))]
		public static class OnAlarmClickedPatch
		{
			public static bool Prefix(ScheduleScreenEntry __instance)
			{
				using var _ = Profiler.Scope();
				if (!ShouldRequestFromClient())
					return true;
				if (!TryGetSchedule(__instance, out Schedule schedule, out int index))
					return false;
				ScheduleSyncCoordinator.SendClientRequest(new ScheduleDetailsUpdatePacket
				{
					ScheduleIndex = index,
					AlarmActivated = !schedule.alarmActivated,
					UpdateType = ScheduleDetailsUpdatePacket.DetailsUpdateType.ALARM_STATE
				});
				return false;
			}

			public static void Postfix()
			{
				ScheduleSyncCoordinator.PublishHostMutation();
			}
		}

		[HarmonyPatch(typeof(ScheduleScreenEntry), nameof(ScheduleScreenEntry.PaintBlock))]
		public static class PaintBlockPatch
		{
			public static bool Prefix(
				ScheduleScreenEntry __instance,
				ScheduleBlockButton blockButton,
				ref bool __result)
			{
				using var _ = Profiler.Scope();
				if (!MultiplayerSession.InSession || MultiplayerSession.IsHost ||
				    ScheduleSyncCoordinator.IsApplyingAuthoritativeMutation)
					return true;
				__result = TrySendPaintRequest(__instance, blockButton);
				return false;
			}

			public static void Postfix(bool __result)
			{
				if (__result)
					ScheduleSyncCoordinator.PublishHostMutation();
			}
		}

		[HarmonyPatch(typeof(ScheduleScreenEntry), nameof(ScheduleScreenEntry.OnResetClicked))]
		public static class OnResetClickedPatch
		{
			public static bool Prefix(ScheduleScreenEntry __instance)
			{
				using var _ = Profiler.Scope();
				if (!ShouldRequestFromClient())
					return true;
				if (!TryGetSchedule(__instance, out Schedule unused, out int index))
					return false;
				ScheduleSyncCoordinator.SendClientRequest(new ScheduleRowPacket
				{
					ScheduleIndex = index,
					Action = ScheduleRowPacket.RowAction.RESET_DEFAULT,
					TimetableToIndex = 0
				});
				return false;
			}

			public static void Postfix()
			{
				ScheduleSyncCoordinator.PublishHostMutation();
			}
		}

		private static bool RequestDeleteOrRun(ScheduleScreenEntry entry)
		{
			using var _ = Profiler.Scope();
			if (!ShouldRequestFromClient())
				return true;
			if (!TryGetSchedule(entry, out Schedule unused, out int index) ||
			    ScheduleManager.Instance.schedules.Count <= 1)
				return false;
			ScheduleSyncCoordinator.SendClientRequest(new ScheduleDeletePacket { ScheduleIndex = index });
			return false;
		}

		private static bool TrySendPaintRequest(
			ScheduleScreenEntry entry,
			ScheduleBlockButton blockButton)
		{
			if (!TryGetSchedule(entry, out Schedule schedule, out int scheduleIndex) ||
			    blockButton == null || ScheduleScreen.Instance == null)
				return false;
			ScheduleGroup group = Db.Get().ScheduleGroups.resources.Find(
				item => item.Id == ScheduleScreen.Instance.SelectedPaint);
			if (group == null)
				return false;

			foreach (var pair in entry.blockButtonsByTimetableRow)
			{
				for (int i = 0; i < pair.Value.Count; i++)
				{
					if (!ReferenceEquals(pair.Value[i], blockButton))
						continue;
					int row = entry.timetableRows.IndexOf(pair.Key);
					int blockIndex = row * ScheduleSyncProtocol.BlocksPerTimetable + i;
					if (row < 0 || blockIndex < 0 || blockIndex >= schedule.blocks.Count ||
					    schedule.blocks[blockIndex].GroupId == group.Id)
						return false;
					ScheduleSyncCoordinator.SendClientRequest(new ScheduleBlockUpdatePacket
					{
						ScheduleIndex = scheduleIndex,
						BlockIndex = blockIndex,
						GroupId = group.Id
					});
					return true;
				}
			}
			return false;
		}

		private static bool TryGetSchedule(
			ScheduleScreenEntry entry,
			out Schedule schedule,
			out int scheduleIndex)
		{
			schedule = entry?.schedule;
			scheduleIndex = schedule?.GetScheduleIndex() ?? -1;
			return schedule != null && scheduleIndex >= 0;
		}

		private static bool ShouldRequestFromClient()
			=> MultiplayerSession.InSession && MultiplayerSession.IsClient &&
			   !ScheduleSyncCoordinator.IsApplyingAuthoritativeMutation;
	}
}
