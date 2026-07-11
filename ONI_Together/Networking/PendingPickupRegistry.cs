using ONI_Together.DebugTools;
using System.Collections.Generic;
using Shared.Profiling;

namespace ONI_Together.Networking
{
	public static class PendingPickupRegistry
	{
		private static readonly HashSet<int> PendingNetIds = [];

		public static void Add(int netId)
		{
			using var _ = Profiler.Scope();
			PendingNetIds.Add(netId);
		}

		public static bool TryConsume(int netId)
		{
			using var _ = Profiler.Scope();
			return PendingNetIds.Remove(netId);
		}

		public static void Clear()
		{
			using var _ = Profiler.Scope();
			int n = PendingNetIds.Count;
			PendingNetIds.Clear();
			DebugConsole.Log($"[PendingPickup] cleared count={n}");
		}
	}
}
