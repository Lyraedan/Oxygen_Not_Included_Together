using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.DebugTools
{
	/// <summary>
	/// Centralized sync metrics for debug display.
	/// Each syncer updates its metric after performing a sync operation.
	/// </summary>
	public static class SyncStats
	{
		public class SyncMetric
		{
			public string Name;
			public float Interval;
			public float LastSyncTime;
			public int LastItemCount;
			public int LastPacketBytes;
			public float LastDurationMs;

			public float TimeRemaining => Mathf.Max(0, Interval - (Time.unscaledTime - LastSyncTime));
		}

		// WorldStateSyncer metrics
		public static SyncMetric Gas = new SyncMetric { Name = "Gas/Liquid", Interval = 1.5f };
		public static SyncMetric Digging = new SyncMetric { Name = "Digging", Interval = 3f };
		public static SyncMetric Chores = new SyncMetric { Name = "Chores", Interval = 3f };
		public static SyncMetric Research = new SyncMetric { Name = "Research", Interval = 3f };
		// Priorities and Disinfect removed - synced via event-driven patches

		// Other syncer metrics
		public static SyncMetric Buildings = new SyncMetric { Name = "Buildings", Interval = 30f };
		public static SyncMetric Structures = new SyncMetric { Name = "Structures", Interval = 0.5f };
		public static SyncMetric VitalStats = new SyncMetric { Name = "VitalStats", Interval = 1f };
		// DragTool: bulk flush observability (count = cells batched in last flush, bytes = payload).
		public static SyncMetric DragTool = new SyncMetric { Name = "DragTool", Interval = 0.1f };
		// AnimSync: host-side per-entity visible-path sends (activity-triggered + interval).
		// LastItemCount = recipients in last send; LastPacketBytes = snapshot bytes.
		public static SyncMetric AnimSync = new SyncMetric { Name = "AnimSync", Interval = 5f };
		// AnimResyncRequest: client-side resync-request packets (count = NetIds requested,
		// bytes = packet size, durationMs = current retry interval in ms for easy log read).
		public static SyncMetric AnimResyncRequest = new SyncMetric { Name = "AnimResyncReq", Interval = 5f };

		public static SyncMetric OxySync = new SyncMetric() { Name = "OxySync", Interval = 0.5f };

		/// <summary>
		/// Updates a metric after a sync operation.
		/// </summary>
		public static void RecordSync(SyncMetric metric, int itemCount, int packetBytes, float durationMs)
		{
			using var _ = Profiler.Scope();

			metric.LastSyncTime = Time.unscaledTime;
			metric.LastItemCount = itemCount;
			metric.LastPacketBytes = packetBytes;
			metric.LastDurationMs = durationMs;
		}

		/// <summary>
		/// All metrics for iteration in debug display.
		/// </summary>
		public static SyncMetric[] AllMetrics => new[]
		{
			Gas, Digging, Chores, Research,
			Buildings, Structures, VitalStats,
			DragTool,
			AnimSync, AnimResyncRequest,
			OxySync
		};
	}
}
