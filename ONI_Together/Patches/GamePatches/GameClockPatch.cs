using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.OxySync.Components;
using System;
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
			GameServerHardSync.ResetCycleCounter();

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
            GameServerHardSync.ResetCycleCounter();
        }

		// Allow clients to advance time smoothly locally; host sync will correct drift
		[HarmonyPatch(nameof(GameClock.AddTime))]
		[HarmonyPrefix]
		public static bool AddTime_Prefix()
		{
			return true;
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
				int currentCycle = __instance.GetCycle();
				bool cycleChanged = currentCycle != _lastCycle;

				// 1. Broadcast world time every 1s (in-game or if time jumped backwards on save load)
				// and immediately on cycle transitions.
				if (cycleChanged || Mathf.Abs(currentTime - _lastSentTime) >= 1f)
				{
					_lastSentTime = currentTime;
					float cycleTime = __instance.GetTimeSinceStartOfCycle();

					GameTimeSyncer.Instance?.BroadcastTime(currentCycle, cycleTime);

					if (MultiplayerSession.ConnectedPlayers.Count > 0)
					{
						PacketSender.SendToAllClients(new ONI_Together.Networking.Packets.World.WorldCyclePacket
						{
							Cycle = currentCycle,
							CycleTime = cycleTime
						}, cycleChanged ? PacketSendMode.Reliable : PacketSendMode.Unreliable);
					}
				}

				// 2. Trigger HardSync at the configured cycle interval
				if (cycleChanged)
				{
					_lastCycle = currentCycle;

					GameServerHardSync.hardSyncDoneThisCycle = false;

					int interval = Configuration.Instance.Host.Server.GetHardSyncCycleInterval();
					DebugConsole.Log($"[HardSync] New cycle detected ({currentCycle}) — auto hard sync interval: {interval} cycle(s).");

					GameServerHardSync.NotifyCycleStarted();
				}
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[GameClockPatch.AddTime_Postfix] {ex}");
			}
		}
	}
}
