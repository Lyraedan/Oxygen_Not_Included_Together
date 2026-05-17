using HarmonyLib;
using ONI_Together.Networking;
using Shared.Profiling;

namespace ONI_Together.Patches.World
{
	[HarmonyPatch(typeof(BatteryTracker), "UpdateData")]
	public static class BatteryTrackerPatch
	{
		private sealed class ClientRefreshScope : System.IDisposable
		{
			public void Dispose()
			{
				_allowedClientRefreshDepth = System.Math.Max(0, _allowedClientRefreshDepth - 1);
			}
		}

		private static int _allowedClientRefreshDepth;

		internal static System.IDisposable AllowClientRefresh()
		{
			_allowedClientRefreshDepth++;
			return new ClientRefreshScope();
		}

		public static bool Prefix(BatteryTracker __instance)
		{
			using var _ = Profiler.Scope();

			// Original client-block existed to avoid hard-sync crashes. IsHardSyncInProgress
			// now covers that case directly, so let BatteryTracker.UpdateData run on clients
			// otherwise — blocking it leaves batteries unregistered in the local CircuitManager,
			// making every powered building render as "no power" until the next joules delta.
			if (GameClient.IsHardSyncInProgress)
				return false;

			return true;
		}
	}
}
