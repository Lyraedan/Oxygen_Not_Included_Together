using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.States;
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

			// A world that is still being built.
			//
			// Measured on a client three milliseconds after "Loaded <save>": a
			// NullReferenceException inside UpdateData, from TrackerTool.Update. The
			// tracker runs while the world is half built and reads something that is not
			// there yet.
			//
			// It matters for the same reason the hard-sync block above does. An exception
			// out of UpdateData leaves batteries unregistered in the local CircuitManager,
			// which is the "every powered building shows no power" state this patch exists
			// to avoid - so throwing here costs what blocking here used to.
			//
			// Skipped rather than caught: one missed tracker update is invisible, and the
			// next one runs a fraction of a second later against a finished world.
			if (Game.Instance == null || Grid.WidthInCells == 0)
				return false;

			// A client that is not playing yet, including the gap in the middle of a
			// reconnect.
			//
			// Testing GameClient.State alone against LoadingWorld was not enough, and the
			// log says why in three consecutive lines in the same millisecond: "State
			// changed to: LoadingWorld", "Disconnected from server", "State changed to:
			// Disconnected". The save transfer finishes and the transport drops while the
			// world loads, so by the time the tracker ran the state being watched for had
			// already been replaced.
			//
			// MultiplayerSession.IsClient is false during that gap too, which is why the
			// cached connection is consulted - it is what the reconnect path itself uses
			// to decide whether to rejoin once the world has loaded. A host is neither,
			// so its behaviour is unchanged.
			bool clientOrReconnecting = MultiplayerSession.IsClient
				|| (!MultiplayerSession.IsHost && GameClient.HasCachedConnection());
			if (clientOrReconnecting && GameClient.State != ClientState.InGame)
				return false;

			return true;
		}
	}
}
