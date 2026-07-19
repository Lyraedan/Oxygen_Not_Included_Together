#if DEBUG
using System;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Patches;

namespace ONI_Together.DebugTools
{
	internal sealed class SoakTickRunConfig
	{
		internal int RunId;
		internal int SampleId;
		internal int TickCount;
	}

	internal sealed class SoakTickCompletion
	{
		internal int RunId;
		internal int SampleId;
		internal int CompletedTicks;
	}

	internal static class SoakTickBarrier
	{
		private const int MaxPauseReleaseAttempts = 64;
		private static bool _prepared;
		private static bool _armed;
		private static int _completedTicks;
		private static SoakTickRunConfig _config;
		private static Action<SoakTickCompletion> _onCompleted;
		private static SoakTickCompletion _pendingCompletion;
		private static Action<SoakTickCompletion> _pendingCallback;
		internal static bool IsControllingSpeed
			=> _prepared || _armed || _pendingCompletion != null;

		internal static bool Prepare(
			SoakTickRunConfig config,
			Action<SoakTickCompletion> onCompleted)
		{
			if (_prepared || _armed || _pendingCompletion != null
			    || config == null || config.RunId <= 0
				|| config.SampleId <= 0 || config.TickCount <= 0 || onCompleted == null
				|| SpeedControlScreen.Instance == null || !SpeedControlScreen.Instance.IsPaused)
			{
				return false;
			}

			_config = config;
			_completedTicks = 0;
			_onCompleted = onCompleted;
			_prepared = true;
			DebugConsole.Log($"[SoakTick][PREPARED] side={LocalSide()} " +
			                 $"run={config.RunId} sample={config.SampleId} ticks={config.TickCount}");
			return true;
		}

		internal static bool StartPrepared(int runId, int sampleId)
		{
			if (!_prepared || _armed || !MatchesCurrent(runId, sampleId)
				|| SpeedControlScreen.Instance == null)
				return false;
			if (!SpeedControlScreen.Instance.IsPaused)
				SetLocalSpeed(paused: true);
			if (!SetLocalSpeed(paused: false))
				return false;
			_armed = true;
			DebugConsole.Log($"[SoakTick][STARTED] side={LocalSide()} " +
			                 $"run={runId} sample={sampleId} ticks={_config.TickCount}");
			return true;
		}

		internal static void Cancel()
		{
			_prepared = false;
			_armed = false;
			_completedTicks = 0;
			_config = null;
			_onCompleted = null;
			_pendingCompletion = null;
			_pendingCallback = null;
		}

		internal static void ResetSessionState()
		{
			Cancel();
			SoakStateHashProbe.ResetClientSegmentSequence();
		}

		internal static bool Cancel(int runId)
		{
			if ((!_prepared && !_armed) || _config?.RunId != runId)
				return false;
			Cancel();
			return true;
		}

		internal static bool MatchesCurrent(int runId, int sampleId)
			=> (_prepared || _armed) && _config?.RunId == runId && _config.SampleId == sampleId;

		internal static bool IsPrepared(int runId, int sampleId)
			=> _prepared && !_armed && _config?.RunId == runId && _config.SampleId == sampleId;

		internal static bool IsNextSegment(
			(int RunId, int SampleId) completed,
			int runId,
			int sampleId)
		{
			return runId > 0 && sampleId > 0
				&& (completed.RunId == 0
					? sampleId == 1
					: runId == completed.RunId && sampleId == completed.SampleId + 1);
		}

		internal static void AfterSimTick()
		{
			CancelIfSessionLost();
			if (!_armed)
				return;
			_completedTicks++;
			if (ShouldLogProgress(_completedTicks, _config.TickCount))
				DebugConsole.Log($"[SoakTick][PROGRESS] side={LocalSide()} " +
				                 $"run={_config.RunId} sample={_config.SampleId} " +
				                 $"ticks={_completedTicks}/{_config.TickCount}");
			if (_completedTicks == _config.TickCount)
				ReachBarrier();
		}

		internal static bool ShouldLogProgress(int completedTicks, int targetTicks)
			=> completedTicks > 0 && targetTicks > 0
			   && (completedTicks == targetTicks || completedTicks % 300 == 0);

		internal static void Pump()
		{
			ResumeInterruptedRun();
			DispatchPendingCompletion();
		}

		internal static bool ShouldResumeInterruptedRun(bool armed, bool isPaused)
			=> armed && isPaused;

		private static void ResumeInterruptedRun()
		{
			if (!ShouldResumeInterruptedRun(
				    _armed, SpeedControlScreen.Instance?.IsPaused == true))
				return;
			DebugConsole.LogWarning($"[SoakTick][RESUME_INTERRUPTED] side={LocalSide()} " +
			                        $"run={_config.RunId} sample={_config.SampleId} " +
			                        $"ticks={_completedTicks}/{_config.TickCount}");
			if (!SetLocalSpeed(paused: false))
				DebugConsole.LogError("[SoakTick] Failed to resume an interrupted fixed-tick run");
		}

		private static void DispatchPendingCompletion()
		{
			if (_pendingCompletion == null)
				return;
			SoakTickCompletion completion = _pendingCompletion;
			Action<SoakTickCompletion> callback = _pendingCallback;
			_pendingCompletion = null;
			_pendingCallback = null;
			DebugConsole.Log($"[SoakTick][DISPATCH] side={LocalSide()} " +
			                 $"run={completion.RunId} sample={completion.SampleId} " +
			                 $"ticks={completion.CompletedTicks}");
			try
			{
				callback?.Invoke(completion);
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[SoakHash] Tick barrier completion failed: {ex}");
			}
		}

		internal static void CancelIfSessionLost()
		{
			if (MultiplayerSession.InSession)
				return;
			if (_prepared || _armed)
			{
				Cancel();
				EnsureLocallyPaused();
			}
			SoakStateHashProbe.ResetClientSegmentSequence();
		}

		internal static void EnsureLocallyPaused() => SetLocalSpeed(paused: true);

		internal static bool ReleaseAllPauseLayers(
			Func<bool> isPaused,
			System.Action unpause)
		{
			for (int attempt = 0;
			     attempt < MaxPauseReleaseAttempts && isPaused();
			     attempt++)
			{
				unpause();
			}
			return !isPaused();
		}

		private static void ReachBarrier()
		{
			SoakTickCompletion completion = new SoakTickCompletion
			{
				RunId = _config.RunId,
				SampleId = _config.SampleId,
				CompletedTicks = _completedTicks,
			};
			_pendingCompletion = completion;
			_pendingCallback = _onCompleted;
			_prepared = false;
			_armed = false;
			if (Game.Instance != null)
				Traverse.Create(Game.Instance).Field("simDt").SetValue(0f);
			SetLocalSpeed(paused: true);
			_config = null;
			_onCompleted = null;
		}

		private static string LocalSide() => MultiplayerSession.IsHost ? "host" : "client";

		private static bool SetLocalSpeed(bool paused)
		{
			SpeedControlScreen speed = SpeedControlScreen.Instance;
			if (speed == null)
				return false;

			SpeedControlScreen_SendSpeedPacketPatch.IsSyncing = true;
			try
			{
				if (paused)
				{
					if (!speed.IsPaused)
						speed.Pause(false);
					return speed.IsPaused;
				}

				speed.SetSpeed((int)Networking.Packets.World.SpeedChangePacket.SpeedState.Triple);
				return ReleaseAllPauseLayers(
					() => speed.IsPaused,
					() => speed.Unpause(false));
			}
			finally
			{
				SpeedControlScreen_SendSpeedPacketPatch.IsSyncing = false;
			}
		}
	}

	[HarmonyPatch(typeof(StateMachineUpdater), nameof(StateMachineUpdater.AdvanceOneSimSubTick))]
	internal static class SoakTickBarrierPatch
	{
		[HarmonyPostfix]
		private static void Postfix() => SoakTickBarrier.AfterSimTick();
	}
}
#endif
