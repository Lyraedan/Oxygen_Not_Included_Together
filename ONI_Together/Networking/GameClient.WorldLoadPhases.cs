using System;
using ONI_Together.DebugTools;
using ONI_Together.Networking.States;
using UnityEngine;

namespace ONI_Together.Networking
{
	internal sealed class WorldBaselineProgressLease
	{
		private readonly long _generation;
		private readonly float _idleTimeoutSeconds;
		private readonly float _absoluteDeadline;
		private float _idleDeadline;
		private int _nextChunkIndex;
		private int _totalChunks;

		internal WorldBaselineProgressLease(
			long generation, float startedAt,
			float idleTimeoutSeconds, float absoluteTimeoutSeconds)
		{
			if (generation <= 0 || !IsFinite(startedAt)
			    || !IsPositiveFinite(idleTimeoutSeconds)
			    || !IsPositiveFinite(absoluteTimeoutSeconds))
				throw new ArgumentOutOfRangeException();
			_generation = generation;
			_idleTimeoutSeconds = idleTimeoutSeconds;
			_idleDeadline = startedAt + idleTimeoutSeconds;
			_absoluteDeadline = startedAt + absoluteTimeoutSeconds;
		}

		internal bool TryAdvance(
			long generation, int chunkIndex, int totalChunks, float now)
		{
			if (generation != _generation || chunkIndex != _nextChunkIndex
			    || totalChunks <= 0 || totalChunks > Packets.World.WorldDataPacket.MaxChunkCount
			    || _totalChunks != 0 && totalChunks != _totalChunks
			    || !IsFinite(now) || IsTimedOut(now))
				return false;
			_totalChunks = totalChunks;
			_nextChunkIndex++;
			_idleDeadline = Math.Min(_absoluteDeadline, now + _idleTimeoutSeconds);
			return true;
		}

		internal bool IsTimedOut(float now)
			=> !IsFinite(now) || now >= _idleDeadline || now >= _absoluteDeadline;

		private static bool IsPositiveFinite(float value)
			=> value > 0f && IsFinite(value);

		private static bool IsFinite(float value)
			=> !float.IsNaN(value) && !float.IsInfinity(value);
	}

	public static partial class GameClient
	{
		private const float MinimumBaselineAbsoluteTimeoutSeconds = 120f;
		private const float BaselineAbsoluteTimeoutMultiplier = 8f;

		private enum WorldLoadPhase
		{
			None,
			LoadingApproval,
			WorldBaseline,
			ReadyAcceptance
		}

		private static WorldLoadPhase _worldLoadPhase;
		private static ulong _worldLoadPhaseToken;
		private static long _worldLoadPhaseGeneration;
		private static float _worldLoadPhaseDeadline;
		private static float _nextWorldLoadRetryAt;
		private static int _worldLoadPhaseRetries;
		private static WorldBaselineProgressLease _worldBaselineProgressLease;

		internal static bool ShouldTerminateConnectionValidation(bool inMenu) => !inMenu;

		internal static bool ShouldRetryReconnectStartFailure(bool inGame, int attempt)
			=> inGame && attempt >= 0 && attempt < MAX_RECONNECT_ATTEMPTS;

		internal static void FailWorldLoadHandshake(string message)
			=> FailConnectionValidation(STRINGS.UI.PROTOCOL.VALIDATION.TITLE, message);

		internal static void BeginLoadingApprovalWait(ulong token, long generation)
			=> BeginWorldLoadPhase(WorldLoadPhase.LoadingApproval, token, generation);

		internal static void EndLoadingApprovalWait(ulong token, long generation)
			=> EndWorldLoadPhase(WorldLoadPhase.LoadingApproval, token, generation);

		internal static void BeginWorldBaselineWait(ulong token, long generation)
			=> BeginWorldLoadPhase(WorldLoadPhase.WorldBaseline, token, generation);

		internal static void EndWorldBaselineWait(ulong token, long generation)
			=> EndWorldLoadPhase(WorldLoadPhase.WorldBaseline, token, generation);

		internal static bool RecordWorldBaselineProgress(
			long generation, int chunkIndex, int totalChunks)
			=> _worldLoadPhase == WorldLoadPhase.WorldBaseline
			   && _worldBaselineProgressLease != null
			   && _worldBaselineProgressLease.TryAdvance(
				   generation, chunkIndex, totalChunks, Time.unscaledTime);

		internal static void BeginReadyAcceptanceWait(ulong token, long generation)
			=> BeginWorldLoadPhase(WorldLoadPhase.ReadyAcceptance, token, generation);

		internal static void CancelWorldLoadPhase()
		{
			_worldLoadPhase = WorldLoadPhase.None;
			_worldLoadPhaseToken = 0;
			_worldLoadPhaseGeneration = 0;
			_worldLoadPhaseDeadline = 0;
			_nextWorldLoadRetryAt = 0;
			_worldLoadPhaseRetries = 0;
			_worldBaselineProgressLease = null;
		}

		private static void BeginWorldLoadPhase(
			WorldLoadPhase phase, ulong token, long generation)
		{
			_worldLoadPhase = phase;
			_worldLoadPhaseToken = token;
			_worldLoadPhaseGeneration = generation;
			_worldLoadPhaseRetries = 0;
			float now = Time.unscaledTime;
			int timeout = Math.Max(10, Configuration.Instance.Client.TimeoutSeconds);
			_worldLoadPhaseDeadline = now + timeout;
			_nextWorldLoadRetryAt = now + RECONNECT_BASE_DELAY;
			if (phase == WorldLoadPhase.WorldBaseline)
			{
				float absoluteTimeout = Math.Max(
					MinimumBaselineAbsoluteTimeoutSeconds,
					timeout * BaselineAbsoluteTimeoutMultiplier);
				_worldBaselineProgressLease = new WorldBaselineProgressLease(
					generation, now, timeout, absoluteTimeout);
			}
		}

		private static void EndWorldLoadPhase(
			WorldLoadPhase phase, ulong token, long generation)
		{
			if (_worldLoadPhase == phase
			    && _worldLoadPhaseToken == token
			    && _worldLoadPhaseGeneration == generation)
				CancelWorldLoadPhase();
		}

		private static void UpdateWorldLoadPhase()
		{
			if (_worldLoadPhase == WorldLoadPhase.None)
				return;
			float now = Time.unscaledTime;
			bool timedOut = _worldLoadPhase == WorldLoadPhase.WorldBaseline
				? _worldBaselineProgressLease == null
				  || _worldBaselineProgressLease.IsTimedOut(now)
				: now >= _worldLoadPhaseDeadline;
			if (timedOut)
			{
				FailWorldLoadPhase("World-load handshake timed out.");
				return;
			}
			if (_worldLoadPhase == WorldLoadPhase.WorldBaseline
			    || now < _nextWorldLoadRetryAt)
				return;
			if (_worldLoadPhaseRetries >= MAX_RECONNECT_ATTEMPTS)
			{
				FailWorldLoadPhase("World-load handshake exhausted its retry budget.");
				return;
			}

			ClientReadyState state = _worldLoadPhase == WorldLoadPhase.LoadingApproval
				? ClientReadyState.Loading
				: ClientReadyState.Ready;
			bool sent = ReadyManager.SendReadyStatusPacket(state);
			_worldLoadPhaseRetries++;
			float delay = Mathf.Min(
				RECONNECT_BASE_DELAY * Mathf.Pow(2, _worldLoadPhaseRetries), 8f);
			_nextWorldLoadRetryAt = now + delay;
			if (!sent)
				DebugConsole.LogWarning($"[GameClient] Retry send failed for {_worldLoadPhase}");
		}

		private static void FailWorldLoadPhase(string message)
		{
			DebugConsole.LogError($"[GameClient] {message}", false);
			FailConnectionValidation(STRINGS.UI.PROTOCOL.VALIDATION.TITLE, message);
		}

		private static void FailConnectionValidation(string reason, string message)
		{
			ReadyManager.TrySendWorldLoadAbort();
			ReadyManager.CancelPendingClientWorldLoad();
			SetState(ClientState.Error);
			Disconnect();
			NetworkConfig.TransportClient.OnReturnToMenu?.Invoke(reason, message);
		}
	}
}
