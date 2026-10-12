using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World;
using System;
using System.Collections;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.GamePatches
{
	[HarmonyPatch(typeof(GameClock))]
	public static class GameClockPatch
	{
		public static bool allowAddTimeForSetTime = false;

		private static float _lastSentTime = 0f;
		private static int _lastCycle = -1;
		private static readonly HardSyncCycleSchedule _hardSyncSchedule = new();

		[HarmonyPatch(nameof(GameClock.OnPrefabInit))]
		[HarmonyPostfix]
		public static void OnPrefabInit_Postfix(GameClock __instance)
		{
			// Initialize as what the game starts at.
			_lastSentTime = __instance.GetTime();
			_lastCycle = __instance.GetCycle();
			ResetAutomaticHardSyncSchedule(__instance.GetCycle());
        }

		[HarmonyPatch(nameof(GameClock.OnDeserialized))]
		[HarmonyPostfix]
		public static void OnDeserialized_Postfix(GameClock __instance)
		{
            // Save loaded
            _lastSentTime = __instance.GetTime();
            _lastCycle = __instance.GetCycle();
			ResetAutomaticHardSyncSchedule(__instance.GetCycle());
        }

		public static void ResetAutomaticHardSyncSchedule()
		{
			ResetAutomaticHardSyncSchedule(GameClock.Instance != null ? GameClock.Instance.GetCycle() : -1);
		}

		private static void ResetAutomaticHardSyncSchedule(int cycle)
		{
			var config = Configuration.Instance;
			_hardSyncSchedule.Reset(cycle, config.HardSyncOnCycleStart, config.HardSyncIntervalCycles);
		}

		public static void UpdateAutomaticHardSyncSettings()
		{
			var config = Configuration.Instance;
			_hardSyncSchedule.UpdateSettings(GameClock.Instance != null ? GameClock.Instance.GetCycle() : -1,
				config.HardSyncOnCycleStart, config.HardSyncIntervalCycles);
		}

		// Prevent clients from running AddTime
		[HarmonyPatch(nameof(GameClock.AddTime))]
		[HarmonyPrefix]
		public static bool AddTime_Prefix()
		{
			using var _ = Profiler.Scope();

			try
			{
				if (!MultiplayerSession.InSession)
					return true;

				if (MultiplayerSession.IsClient && !allowAddTimeForSetTime)
					return false;

				return true;
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[GameClockPatch.AddTime_Prefix] {ex}");
				return true;
			}
		}

		// Host logic: send WorldCyclePacket every 1s and schedule automatic hard syncs.
		[HarmonyPatch(nameof(GameClock.AddTime))]
		[HarmonyPostfix]
		public static void AddTime_Postfix(GameClock __instance)
		{
			using var _ = Profiler.Scope();

			try
			{
				if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost)
					return;

				float currentTime = __instance.GetTime();

				// 1. Broadcast world time every 1s
				if (currentTime - _lastSentTime >= 1f)
				{
					_lastSentTime = currentTime;

					PacketSender.SendToAllClients(new WorldCyclePacket
					{
						Cycle = __instance.GetCycle(),
						CycleTime = __instance.GetTimeSinceStartOfCycle()
					}, PacketSendMode.Unreliable);
				}

				// 2. Reset manual-sync allowance each cycle; automatic sync has its own interval.
				int currentCycle = __instance.GetCycle();
				if (currentCycle != _lastCycle)
				{
					_lastCycle = currentCycle;

					GameServerHardSync.hardSyncDoneThisCycle = false;
				}

				var config = Configuration.Instance;
				if (_hardSyncSchedule.Advance(currentCycle, config.HardSyncOnCycleStart, config.HardSyncIntervalCycles)
					&& MultiplayerSession.SessionHasPlayers && !GameServerHardSync.IsHardSyncInProgress)
				{
					DebugConsole.Log($"[HardSync] Scheduling automatic sync at cycle {currentCycle} (interval: {config.HardSyncIntervalCycles}).");
					CoroutineRunner.RunOne(DelayedHardSync(__instance, _hardSyncSchedule.Revision));
				}
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[GameClockPatch.AddTime_Postfix] {ex}");
			}
		}

		private static IEnumerator DelayedHardSync(GameClock clock, int revision)
		{
			using var _ = Profiler.Scope();

			yield return new WaitForSecondsRealtime(5f); // wait to ensure ONI's autosave completes (generous wait time)

			var config = Configuration.Instance;
			if (clock == null || GameClock.Instance != clock || !MultiplayerSession.IsHostInSession
				|| !MultiplayerSession.SessionHasPlayers || GameServerHardSync.IsHardSyncInProgress
				|| !_hardSyncSchedule.IsCurrent(revision, config.HardSyncOnCycleStart, config.HardSyncIntervalCycles))
				yield break;

			GameServerHardSync.PerformHardSync(false);
		}
	}
}
