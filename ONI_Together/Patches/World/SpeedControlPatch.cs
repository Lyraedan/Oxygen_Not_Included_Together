using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.OxySync.Components;
using Shared.OxySync;
using System;
using Shared.Profiling;

namespace ONI_Together.Patches
{
	[HarmonyPatch(typeof(SpeedControlScreen))]
	public static class SpeedControlScreen_SendSpeedPacketPatch
	{
		public static bool IsSyncing = false;

		[HarmonyPatch("OnPrefabInit")]
		[HarmonyPostfix]
		public static void OnPrefabInit_Postfix(SpeedControlScreen __instance)
		{
			if (!__instance.TryGetComponent<GameSpeedSyncComponent>(out _))
			{
				var identity = __instance.gameObject.AddComponent<NetworkIdentity>();
				identity.NetId = PredeterminedNetIds.Speed_Control_Screen;
				NetworkIdentityRegistry.RegisterOverride(identity, PredeterminedNetIds.Speed_Control_Screen);
				__instance.gameObject.AddComponent<GameSpeedSyncComponent>();
			}
		}

		[HarmonyPatch("SetSpeed")]
		[HarmonyPostfix]
		public static void SetSpeed_Postfix(int Speed)
		{
			using var _ = Profiler.Scope();

			try
			{
				if (IsSyncing) return;
				if (!MultiplayerSession.InSession) return;

				GameSpeedSyncComponent.Instance?.RequestSetSpeed(Speed);
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[SpeedControlPatch.SetSpeed_Postfix] {ex}");
			}
		}

		[HarmonyPatch(nameof(SpeedControlScreen.TogglePause))]
		[HarmonyPostfix]
		public static void TogglePause_Postfix()
		{
			using var _ = Profiler.Scope();

			try
			{
				if (IsSyncing) return;
				if (!MultiplayerSession.InSession) return;

				// Original TogglePause has already run. Determine the resulting state.
				var newState = SpeedControlScreen.Instance.IsPaused
					? (int)GameSpeedSyncComponent.SpeedState.Paused
					: SpeedControlScreen.Instance.GetSpeed();

				GameSpeedSyncComponent.Instance?.RequestSetSpeed(newState);
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[SpeedControlPatch.TogglePause_Postfix] {ex}");
			}
		}
	}
}
