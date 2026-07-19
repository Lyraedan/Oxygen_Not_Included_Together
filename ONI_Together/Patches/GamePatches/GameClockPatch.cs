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

		[HarmonyPatch(nameof(GameClock.OnPrefabInit))]
		[HarmonyPostfix]
		public static void OnPrefabInit_Postfix(GameClock __instance)
		{
			// Initialize as what the game starts at.
			_lastSentTime = __instance.GetTime();
			_lastCycle = __instance.GetCycle();
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

		// Host logic: send WorldCyclePacket every 1s and schedule the cycle checkpoint.
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
						CycleTime = __instance.GetTimeSinceStartOfCycle(),
						Revision = NetworkIdentityRegistry.NextAuthorityRevision()
					}, PacketSendMode.Unreliable);
				}

				// 2. Check a causally fenced snapshot after the cycle-start autosave.
				int currentCycle = __instance.GetCycle();
				if (currentCycle != _lastCycle)
				{
					_lastCycle = currentCycle;

					GameServerHardSync.hardSyncDoneThisCycle = false;

					DebugConsole.Log($"[ProductionDesync] New cycle detected ({currentCycle}); scheduling checkpoint.");
					CoroutineRunner.RunOne(DelayedCycleCheckpoint(
						currentCycle, Configuration.Instance.HardSyncOnCycleStart));
				}
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[GameClockPatch.AddTime_Postfix] {ex}");
			}
		}

		private static IEnumerator DelayedCycleCheckpoint(int cycle, bool forceHardSync)
		{
			using var _ = Profiler.Scope();

			yield return new WaitForSecondsRealtime(5f); // wait to ensure ONI's autosave completes (generous wait time)
			if (!MultiplayerSession.IsHostInSession || GameClock.Instance?.GetCycle() != cycle)
				yield break;
			if (forceHardSync)
				GameServerHardSync.PerformHardSync(false);
			else
				ProductionDesyncRecovery.TryBeginCycleProbe(cycle);
		}
	}
}
