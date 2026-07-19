#if DEBUG
using ONI_Together.Networking.Components;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class SoakWorldScanFreezeTests
	{
		[UnitTest(name: "Soak checkpoint pauses periodic world scan production", category: "Networking")]
		public static UnitTestResult CheckpointPausesWorldScanProduction()
		{
			WorldStateSyncer.SetWorldScanPaused(true);
			try
			{
				return WorldStateSyncer.WorldScanPausedForTests
				       && !WorldStateSyncer.ShouldRunWorldScan(true)
				       && WorldStateSyncer.ShouldRunWorldScan(false)
					? UnitTestResult.Pass("Frozen checkpoints exclude periodic world scan writes")
					: UnitTestResult.Fail("Periodic world scan could mutate a frozen checkpoint");
			}
			finally
			{
				WorldStateSyncer.SetWorldScanPaused(false);
			}
		}
	}
}
#endif
