using HarmonyLib;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Social;
using Shared.Profiling;
using System;

namespace ONI_Together.Patches.Social
{
	public static class SchedulePatch
	{
		[HarmonyPatch(typeof(Schedule), nameof(Schedule.SetBlockGroup))]
		public static class SetBlockGroupPatch
		{
			public static void Postfix()
			{
				using var _ = Profiler.Scope();
				ScheduleSyncCoordinator.PublishHostMutation();
			}
		}

		[HarmonyPatch(typeof(ScheduleManager), nameof(ScheduleManager.AddSchedule))]
		public static class AddSchedulePatch
		{
			public static void Postfix()
			{
				using var _ = Profiler.Scope();
				ScheduleSyncCoordinator.PublishHostMutation();
			}
		}

		[HarmonyPatch(typeof(ScheduleManager), nameof(ScheduleManager.DuplicateSchedule))]
		public static class DuplicateSchedulePatch
		{
			public static void Postfix()
			{
				using var _ = Profiler.Scope();
				ScheduleSyncCoordinator.PublishHostMutation();
			}
		}

		[HarmonyPatch(typeof(ScheduleManager), nameof(ScheduleManager.DeleteSchedule))]
		public static class DeleteSchedulePatch
		{
			public static void Prefix(
				ScheduleManager __instance,
				Schedule schedule,
				out bool __state)
			{
				using var _ = Profiler.Scope();
				__state = __instance?.schedules != null && __instance.schedules.Count > 1 &&
				          schedule != null && __instance.schedules.Contains(schedule);
				ScheduleSyncCoordinator.BeginHostMutationBatch();
			}

			public static void Postfix(bool __state)
			{
				if (__state)
					ScheduleSyncCoordinator.PublishHostMutation();
			}

			public static Exception Finalizer(Exception __exception)
			{
				ScheduleSyncCoordinator.EndHostMutationBatch();
				return __exception;
			}
		}

		[HarmonyPatch(typeof(ScheduleManager), nameof(ScheduleManager.OnAddDupe))]
		public static class OnAddDupePatch
		{
			public static bool Prefix()
			{
				if (MultiplayerSession.InSession && MultiplayerSession.IsClient &&
				    !ScheduleSyncCoordinator.IsApplyingAuthoritativeMutation)
					return false;
				ScheduleSyncCoordinator.BeginHostMutationBatch();
				return true;
			}

			public static Exception Finalizer(Exception __exception)
			{
				ScheduleSyncCoordinator.EndHostMutationBatch();
				return __exception;
			}
		}

		[HarmonyPatch(typeof(ScheduleManager), nameof(ScheduleManager.AddDefaultSchedule))]
		public static class AddDefaultSchedulePatch
		{
			public static void Prefix()
			{
				ScheduleSyncCoordinator.BeginHostMutationBatch();
			}

			public static Exception Finalizer(Exception __exception)
			{
				ScheduleSyncCoordinator.EndHostMutationBatch();
				return __exception;
			}
		}

		[HarmonyPatch(typeof(ScheduleManager), nameof(ScheduleManager.OnRemoveDupe))]
		public static class OnRemoveDupePatch
		{
			public static void Postfix()
			{
				using var _ = Profiler.Scope();
				ScheduleSyncCoordinator.PublishHostMutation();
			}
		}

		[HarmonyPatch(typeof(ScheduleManager), nameof(ScheduleManager.OnStoredDupeDestroyed))]
		public static class OnStoredDupeDestroyedPatch
		{
			public static void Postfix()
			{
				using var _ = Profiler.Scope();
				ScheduleSyncCoordinator.PublishHostMutation();
			}
		}

		[HarmonyPatch(typeof(ScheduleScreen), nameof(ScheduleScreen.OnAddScheduleClick))]
		public static class OnAddScheduleClickPatch
		{
			public static bool Prefix()
			{
				using var _ = Profiler.Scope();
				if (!MultiplayerSession.InSession)
					return true;
				if (!ScheduleSyncCoordinator.CanAddSchedule())
					return false;
				if (MultiplayerSession.IsHost)
					return true;

				ScheduleSyncCoordinator.SendClientRequest(new ScheduleAddPacket
				{
					Duplicated = false,
					SourceScheduleIndex = -1
				});
				return false;
			}
		}
	}
}
