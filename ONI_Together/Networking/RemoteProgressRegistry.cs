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
		public bool ShowProgressBar;
		public float WorkTimeRemaining;
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

			_states[key] = new RemoteProgressState
			{
				PercentComplete = Mathf.Clamp01(percentComplete),
				ShowProgressBar = showProgressBar,
				WorkTimeRemaining = workTimeRemaining,
				WorkTimeTotal = workTimeTotal,
				ExpireAt = Time.unscaledTime + ENTRY_TTL
			};
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

			if (Time.unscaledTime <= state.ExpireAt)
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
