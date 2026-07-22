using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.OxySync.Components;
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

		[HarmonyPatch(nameof(GameClock.OnPrefabInit))]
		[HarmonyPostfix]
		public static void OnPrefabInit_Postfix(GameClock __instance)
		{
			_lastSentTime = __instance.GetTime();
			_lastCycle = __instance.GetCycle();

			// Attach OxySync time sync component directly to GameClock
			if (!__instance.TryGetComponent<GameTimeSyncer>(out var gtsc))
				__instance.gameObject.AddComponent<GameTimeSyncer>();
        }

		[HarmonyPatch(nameof(GameClock.OnDeserialized))]
		[HarmonyPostfix]
		public static void OnDeserialized_Postfix(GameClock __instance)
		{
            // Save loaded
            _lastSentTime = __instance.GetTime();
            _lastCycle = __instance.GetCycle();
        }

		// Prevent clients from running AddTime
		[HarmonyPatch(nameof(GameClock.AddTime))]
		[HarmonyPrefix]
		public static bool AddTime_Prefix()
		{
			using var _ = Profiler.Scope();

			try
			{
				if (!MultiplayerSession.InActiveSession)
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

		// Host logic: send WorldCyclePacket every 1s and trigger HardSync at cycle start
		[HarmonyPatch(nameof(GameClock.AddTime))]
		[HarmonyPostfix]
		public static void AddTime_Postfix(GameClock __instance)
		{
			using var _ = Profiler.Scope();

			try
			{
				if (!MultiplayerSession.InActiveSession || !MultiplayerSession.IsHost)
					return;

				float currentTime = __instance.GetTime();

				// 1. Broadcast world time every 1s via OxySync ClientRpc
				if (currentTime - _lastSentTime >= 1f)
				{
					_lastSentTime = currentTime;

					GameTimeSyncer.Instance?.BroadcastTime(
						__instance.GetCycle(),
						__instance.GetTimeSinceStartOfCycle());
				}

				// 2. Trigger HardSync at the start of a new cycle
				int currentCycle = __instance.GetCycle();
				if (currentCycle != _lastCycle)
				{
					_lastCycle = currentCycle;

					GameServerHardSync.hardSyncDoneThisCycle = false;

					DebugConsole.Log($"[HardSync] New cycle detected ({currentCycle}) — Hard Sync disabled.");

                    if (Configuration.Instance.HardSyncOnCycleStart)
                        CoroutineRunner.RunOne(DelayedHardSync());
				}
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[GameClockPatch.AddTime_Postfix] {ex}");
			}
		}

		private static IEnumerator DelayedHardSync()
		{
			using var _ = Profiler.Scope();

			yield return new WaitForSecondsRealtime(5f); // wait to ensure ONI's autosave completes (generous wait time)
			GameServerHardSync.PerformHardSync(false);
		}
	}
}
