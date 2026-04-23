using HarmonyLib;
using ONI_MP.DebugTools;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.World;
using System;
using System.Collections;
using Shared.Profiling;
using UnityEngine;
using ONI_MP.Misc.World;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.GamePatches
{
	[HarmonyPatch(typeof(GameClock))]
	public static class GameClockPatch
	{
		public static bool allowAddTimeForSetTime = false;

		private static float _lastSentTime = 0f;
		private static int _lastCycle = -1;

		// Prevent clients from running AddTime
		[HarmonyPatch("AddTime")]
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

		// Host logic: send WorldCyclePacket every 1s and trigger HardSync at cycle start
		[HarmonyPatch("AddTime")]
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

				// 2. Trigger HardSync at the start of a new cycle
				int currentCycle = __instance.GetCycle();
				if (currentCycle != _lastCycle)
				{
					_lastCycle = currentCycle;

					GameServerHardSync.hardSyncDoneThisCycle = false;

					DebugConsole.Log($"[HardSync] New cycle detected ({currentCycle}) — Hard Sync disabled.");

					// Hard Sync Removed by request
					// CoroutineRunner.RunOne(DelayedHardSync());
				}
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[GameClockPatch.AddTime_Postfix] {ex}");
			}
		}

	}
}
