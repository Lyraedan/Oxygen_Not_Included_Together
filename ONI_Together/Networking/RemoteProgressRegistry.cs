using Shared.Profiling;
using System.Collections.Generic;
using UnityEngine;

namespace ONI_Together.Networking
{
	internal enum RemoteProgressKind
	{
		WorkablePercent = 0,
		ComplexFabricatorOrder = 1
	}

	internal struct RemoteProgressState
	{
		public float PercentComplete;
		public float PrevPercentComplete;
		public float UpdateTime;
		public float PrevUpdateTime;
		public bool ShowProgressBar;
		public float WorkTimeRemaining;
		public float PrevWorkTimeRemaining;
		public float WorkTimeTotal;
		public float ExpireAt;
	}

	internal static class RemoteProgressRegistry
	{
		private struct RemoteProgressKey
		{
			public int NetId;
			public RemoteProgressKind Kind;

			public override int GetHashCode()
			{
				return unchecked((NetId * 397) ^ (int)Kind);
			}
		}

		private const float ENTRY_TTL = 1.5f;
		private static readonly Dictionary<RemoteProgressKey, RemoteProgressState> _states = new();

		public static void SetProgress(int netId, RemoteProgressKind kind, float percentComplete, bool showProgressBar, float workTimeRemaining, float workTimeTotal)
		{
			using var _ = Profiler.Scope();

			var key = new RemoteProgressKey
			{
				NetId = netId,
				Kind = kind
			};

			float now = Time.time;
			float clamped = Mathf.Clamp01(percentComplete);
			if (_states.TryGetValue(key, out var existing))
			{
				_states[key] = new RemoteProgressState
				{
					PercentComplete = clamped,
					PrevPercentComplete = existing.PercentComplete,
					UpdateTime = now,
					PrevUpdateTime = existing.UpdateTime,
					ShowProgressBar = showProgressBar,
					WorkTimeRemaining = workTimeRemaining,
					PrevWorkTimeRemaining = existing.WorkTimeRemaining,
					WorkTimeTotal = workTimeTotal,
					ExpireAt = now + ENTRY_TTL
				};
			}
			else
			{
				_states[key] = new RemoteProgressState
				{
					PercentComplete = clamped,
					PrevPercentComplete = clamped,
					UpdateTime = now,
					PrevUpdateTime = now,
					ShowProgressBar = showProgressBar,
					WorkTimeRemaining = workTimeRemaining,
					PrevWorkTimeRemaining = workTimeRemaining,
					WorkTimeTotal = workTimeTotal,
					ExpireAt = now + ENTRY_TTL
				};
			}
		}

		public static bool TryGetState(int netId, RemoteProgressKind kind, out RemoteProgressState state)
		{
			using var _ = Profiler.Scope();

			var key = new RemoteProgressKey
			{
				NetId = netId,
				Kind = kind
			};

			if (!_states.TryGetValue(key, out state))
			{
				return false;
			}

			if (Time.time <= state.ExpireAt)
			{
				return true;
			}

			_states.Remove(key);
			HideTarget(netId, kind);
			state = default;
			return false;
		}

		public static bool TryGetPercent(int netId, RemoteProgressKind kind, out float percentComplete)
		{
			using var _ = Profiler.Scope();

			if (TryGetState(netId, kind, out var state))
			{
				// Pause guard: when game is paused, freeze bar (host WorkTick gets dt==0, so no progress).
				// Time.time is scaled and also freezes, but SpeedControlScreen.IsPaused is the authoritative ONI pause flag.
				if (Time.timeScale <= 0.0001f || (SpeedControlScreen.Instance != null && SpeedControlScreen.Instance.IsPaused))
				{
					percentComplete = state.PercentComplete;
					return true;
				}

				float now = Time.time;
				float elapsedSinceUpdate = now - state.UpdateTime;
				if (elapsedSinceUpdate < 0f)
					elapsedSinceUpdate = 0f;

				// Clamp extrapolation window to avoid overshoot during packet loss (1.5x SEND_INTERVAL)
				const float MAX_EXTRAPOLATION = 0.75f;
				float clampedElapsed = Mathf.Min(elapsedSinceUpdate, MAX_EXTRAPOLATION);

				// Try speed-based extrapolation using the last two host samples
				float deltaTime = state.UpdateTime - state.PrevUpdateTime;
				if (deltaTime > 0.01f)
				{
					float deltaPercent = state.PercentComplete - state.PrevPercentComplete;
					// Detect reset / new work (percent dropped significantly) -> don't extrapolate
					if (deltaPercent < -0.05f)
					{
						percentComplete = state.PercentComplete;
						return true;
					}
					if (deltaPercent < 0f)
						deltaPercent = 0f;

					float speed = deltaPercent / deltaTime; // percent per second
					if (speed > 0.001f)
					{
						// Clamp speed to avoid huge jumps on lag spikes
						speed = Mathf.Min(speed, 5f);
						float interpolated = state.PercentComplete + speed * clampedElapsed;
						percentComplete = Mathf.Clamp01(interpolated);
						return true;
					}
				}

				// Fallback: decay WorkTimeRemaining at 1x real-time speed (handles first packet / zero delta)
				if (state.WorkTimeTotal > 0.01f && state.WorkTimeRemaining >= 0f)
				{
					float interpolatedRemaining = Mathf.Max(0f, state.WorkTimeRemaining - clampedElapsed);
					float pct = 1f - interpolatedRemaining / state.WorkTimeTotal;
					pct = Mathf.Clamp01(pct);
					// Never go backwards below the last authoritative percent
					if (pct < state.PercentComplete)
						pct = state.PercentComplete;
					percentComplete = pct;
					return true;
				}

				percentComplete = state.PercentComplete;
				return true;
			}

			percentComplete = 0f;
			return false;
		}

		public static void Clear(int netId, RemoteProgressKind? kind = null, bool hideTarget = true)
		{
			using var _ = Profiler.Scope();

			if (kind.HasValue)
			{
				ClearEntry(netId, kind.Value, hideTarget);
				return;
			}

			ClearEntry(netId, RemoteProgressKind.WorkablePercent, hideTarget);
			ClearEntry(netId, RemoteProgressKind.ComplexFabricatorOrder, hideTarget);
		}

		public static void ClearAll()
		{
			using var _ = Profiler.Scope();

			_states.Clear();
		}

		private static void ClearEntry(int netId, RemoteProgressKind kind, bool hideTarget)
		{
			using var _ = Profiler.Scope();

			var key = new RemoteProgressKey
			{
				NetId = netId,
				Kind = kind
			};

			if (!_states.Remove(key))
			{
				return;
			}

			if (hideTarget)
			{
				HideTarget(netId, kind);
			}
		}

		private static void HideTarget(int netId, RemoteProgressKind kind)
		{
			using var _ = Profiler.Scope();

			if (!NetworkIdentityRegistry.TryGet(netId, out var identity) || identity == null || identity.gameObject.IsNullOrDestroyed())
			{
				return;
			}

			switch (kind)
			{
				case RemoteProgressKind.WorkablePercent:
					if (identity.TryGetComponent<Workable>(out var workable) && !workable.IsNullOrDestroyed())
					{
						workable.ShowProgressBar(false);
					}
					break;

				case RemoteProgressKind.ComplexFabricatorOrder:
					if (identity.TryGetComponent<ComplexFabricator>(out var fabricator) && !fabricator.IsNullOrDestroyed())
					{
						fabricator.ShowProgressBar(false);
					}
					break;
			}
		}
	}
}
