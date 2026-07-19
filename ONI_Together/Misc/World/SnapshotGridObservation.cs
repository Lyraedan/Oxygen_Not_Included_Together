using System;
using System.Collections.Generic;
using ONI_Together.Networking;
using UnityEngine;

namespace ONI_Together.Misc.World
{
	internal struct SnapshotGridCell : IEquatable<SnapshotGridCell>
	{
		internal int Cell;
		internal ushort ElementIdx;
		internal float Temperature;
		internal float Mass;
		internal byte DiseaseIdx;
		internal int DiseaseCount;

		internal SnapshotGridCell Normalized()
		{
			SnapshotGridCell normalized = this;
			if (normalized.Mass == 0f)
			{
				normalized.Mass = 0f;
				normalized.Temperature = 0f;
			}
			return normalized;
		}

		public bool Equals(SnapshotGridCell other)
			=> Cell == other.Cell && ElementIdx == other.ElementIdx
			   && Temperature == other.Temperature && Mass == other.Mass
			   && DiseaseIdx == other.DiseaseIdx && DiseaseCount == other.DiseaseCount;

		public override bool Equals(object obj)
			=> obj is SnapshotGridCell other && Equals(other);

		public override int GetHashCode() => Cell;
	}

	internal enum SnapshotGridObservationResult
	{
		Waiting,
		Completed,
		TimedOut,
		Finished,
	}

	internal sealed class SnapshotGridObservationSession
	{
		private readonly IReadOnlyList<SnapshotGridCell> _targets;
		private readonly float _deadline;
		private readonly int _scanBudget;
		private int _scanIndex;
		private bool _finished;

		internal SnapshotGridObservationSession(
			IReadOnlyList<SnapshotGridCell> targets,
			float deadline,
			int scanBudget)
		{
			_targets = targets ?? throw new ArgumentNullException(nameof(targets));
			if (float.IsNaN(deadline) || float.IsInfinity(deadline))
				throw new ArgumentOutOfRangeException(nameof(deadline));
			if (scanBudget <= 0)
				throw new ArgumentOutOfRangeException(nameof(scanBudget));
			_deadline = deadline;
			_scanBudget = scanBudget;
		}

		internal SnapshotGridObservationResult Poll(
			Func<SnapshotGridCell, bool> matches, float now)
		{
			if (matches == null)
				throw new ArgumentNullException(nameof(matches));
			if (_finished)
				return SnapshotGridObservationResult.Finished;
			if (now >= _deadline)
				return Finish(SnapshotGridObservationResult.TimedOut);

			int end = Math.Min(_scanIndex + _scanBudget, _targets.Count);
			for (; _scanIndex < end; _scanIndex++)
			{
				if (matches(_targets[_scanIndex]))
					continue;
				_scanIndex = 0;
				return SnapshotGridObservationResult.Waiting;
			}
			return _scanIndex == _targets.Count
				? Finish(SnapshotGridObservationResult.Completed)
				: SnapshotGridObservationResult.Waiting;
		}

		private SnapshotGridObservationResult Finish(SnapshotGridObservationResult result)
		{
			_finished = true;
			return result;
		}
	}

	internal sealed class SnapshotGridObservationCallbacks
	{
		internal System.Action Completed;
		internal System.Action TimedOut;
	}

	internal static class SnapshotGridObservation
	{
		private const int ScanBudgetPerFrame = 32768;
		private static long _generation;
		private static int _maxTargets;
		private static Dictionary<int, SnapshotGridCell> _collectedTargets;
		private static SnapshotGridObservationSession _session;
		private static System.Action _onCompleted;
		private static System.Action _onTimedOut;
		private static SnapshotGridObservationPump _pump;

		internal static void BeginCollection(long generation, int maxTargets)
		{
			Clear();
			_generation = generation;
			_maxTargets = maxTargets;
			_collectedTargets = new Dictionary<int, SnapshotGridCell>();
		}

		internal static bool TryAddTargets(
			long generation, IReadOnlyList<SnapshotGridCell> targets)
		{
			if (generation != _generation || targets == null || _collectedTargets == null
			    || targets.Count > _maxTargets - _collectedTargets.Count)
				return false;
			foreach (SnapshotGridCell target in targets)
			{
				if (_collectedTargets.ContainsKey(target.Cell))
					return false;
				_collectedTargets.Add(target.Cell, target);
			}
			return true;
		}

		internal static bool TryObserve(
			long generation, float timeoutSeconds,
			SnapshotGridObservationCallbacks callbacks)
		{
			if (generation != _generation || _collectedTargets == null
			    || callbacks?.Completed == null || callbacks.TimedOut == null)
				return false;
			var targets = new List<SnapshotGridCell>(_collectedTargets.Values);
			targets.Sort((left, right) => left.Cell.CompareTo(right.Cell));
			_collectedTargets = null;
			_session = new SnapshotGridObservationSession(
				targets, Time.unscaledTime + timeoutSeconds, ScanBudgetPerFrame);
			_onCompleted = callbacks.Completed;
			_onTimedOut = callbacks.TimedOut;
			EnsurePump();
			return true;
		}

		internal static void Cancel() => Clear();

		internal static void Tick(float now)
		{
			if (_session == null)
				return;
			if (!ReadyManager.IsCurrentClientSnapshot(_generation))
			{
				Clear();
				return;
			}

			SnapshotGridObservationResult result = _session.Poll(MatchesGrid, now);
			if (result == SnapshotGridObservationResult.Completed)
				InvokeTerminal(_onCompleted);
			else if (result == SnapshotGridObservationResult.TimedOut)
				InvokeTerminal(_onTimedOut);
		}

		private static bool MatchesGrid(SnapshotGridCell expected)
		{
			if (!Grid.IsValidCell(expected.Cell))
				return false;
			float mass = Grid.Mass[expected.Cell];
			var observed = new SnapshotGridCell
			{
				Cell = expected.Cell,
				ElementIdx = Grid.ElementIdx[expected.Cell],
				Temperature = Grid.Temperature[expected.Cell],
				Mass = mass,
				DiseaseIdx = Grid.DiseaseIdx[expected.Cell],
				DiseaseCount = Grid.DiseaseCount[expected.Cell],
			}.Normalized();
			return observed.Equals(expected);
		}

		private static void EnsurePump()
		{
			if (_pump != null)
				return;
			var gameObject = new GameObject("ONI_Together_SnapshotGridObservation");
			UnityEngine.Object.DontDestroyOnLoad(gameObject);
			_pump = gameObject.AddComponent<SnapshotGridObservationPump>();
		}

		private static void InvokeTerminal(System.Action callback)
		{
			Clear();
			callback?.Invoke();
		}

		private static void Clear()
		{
			_generation = 0;
			_maxTargets = 0;
			_collectedTargets = null;
			_session = null;
			_onCompleted = null;
			_onTimedOut = null;
		}
	}

	internal sealed class SnapshotGridObservationPump : MonoBehaviour
	{
		private void Update()
			=> SnapshotGridObservation.Tick(Time.unscaledTime);
	}
}
