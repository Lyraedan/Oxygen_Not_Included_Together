using ONI_Together.DebugTools;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking
{
	public static class NetIdHelper
	{
		/// <summary>
		/// Generates a deterministic NetID for a building based on its location and object layer.
		/// Range: 1,000,000,000+
		/// </summary>
		public static int GetDeterministicBuildingId(GameObject go)
		{
			using var _ = Profiler.Scope();

			if (go == null) return 0;

			int cell = Grid.PosToCell(go);
			if (!Grid.IsValidCell(cell)) return 0;

			int objectLayer = go.TryGetComponent<Building>(out var building)
				? (int)building.Def.ObjectLayer
				: -1;
			return EnsureNonZero(Shared.NetworkingHash.ForString(
				$"building|{cell}|{go.PrefabID()}|{objectLayer}"));
		}
		public static int GetDeterministicWorkableId(GameObject go)
		{
			using var _ = Profiler.Scope();

			if (go == null) return 0;

			int cell = Grid.PosToCell(go);
			if (!Grid.IsValidCell(cell)) return 0;

			if (!go.TryGetComponent<Workable>(out var workable))
				return 0;

			Workable[] workables = go.GetComponents<Workable>();
			int componentIndex = System.Array.IndexOf(workables, workable);
			int hash = EnsureNonZero(Shared.NetworkingHash.ForString(
				$"workable|{cell}|{go.PrefabID()}|{workable.GetType().FullName}|{componentIndex}"));
			DebugConsole.Log($"Registered workable {go.PrefabID().ToString()} with id: {hash} for workable type {workable.GetType().Name} at cell {cell}");
			return hash;
		}

		public static int GetDeterministicUprootableId(GameObject go)
		{
			using var _ = Profiler.Scope();

			if (go == null || !go.TryGetComponent<Uprootable>(out Uprootable uprootable)) return 0;
			int cell = Grid.PosToCell(go);
			if (!Grid.IsValidCell(cell)) return 0;

			return EnsureNonZero(Shared.NetworkingHash.ForString(
				$"uprootable|{cell}|{go.PrefabID()}"));
		}

		private static int EnsureNonZero(int value) => value == 0 ? 1 : value;
	}
}
