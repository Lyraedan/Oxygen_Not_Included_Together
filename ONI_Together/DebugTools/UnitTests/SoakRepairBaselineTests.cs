#if DEBUG
using System.IO;
using ONI_Together.Misc.World;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class SoakRepairBaselineTests
	{
		[UnitTest(name: "Soak barrier releases every nested pause layer", category: "Networking")]
		public static UnitTestResult ReleasesEveryNestedPauseLayer()
		{
			int pauseDepth = 3;
			int unpauseCalls = 0;
			bool released = SoakTickBarrier.ReleaseAllPauseLayers(
				() => pauseDepth > 0,
				() =>
				{
					pauseDepth--;
					unpauseCalls++;
				});

			return released && pauseDepth == 0 && unpauseCalls == 3
				? UnitTestResult.Pass("The fixed-step run clears every nested pause layer")
				: UnitTestResult.Fail(
					$"Nested pause remained: released={released}, depth={pauseDepth}, calls={unpauseCalls}");
		}

		[UnitTest(name: "Soak tick progress reports fixed intervals", category: "Networking")]
		public static UnitTestResult ReportsFixedTickProgress()
		{
			if (SoakTickBarrier.ShouldLogProgress(299, 1800)
			    || !SoakTickBarrier.ShouldLogProgress(300, 1800)
			    || !SoakTickBarrier.ShouldLogProgress(1800, 1800))
				return UnitTestResult.Fail("Tick barrier progress interval is not observable");
			return UnitTestResult.Pass("Tick barrier exposes progress every 300 simulation ticks");
		}

		[UnitTest(name: "Soak barrier resumes interrupted local simulation", category: "Networking")]
		public static UnitTestResult ResumesInterruptedLocalSimulation()
		{
			if (!SoakTickBarrier.ShouldResumeInterruptedRun(armed: true, isPaused: true)
			    || SoakTickBarrier.ShouldResumeInterruptedRun(armed: false, isPaused: true)
			    || SoakTickBarrier.ShouldResumeInterruptedRun(armed: true, isPaused: false))
				return UnitTestResult.Fail("Fixed-tick ownership cannot distinguish an interruption");
			return UnitTestResult.Pass("Only an armed paused barrier resumes its local simulation");
		}

		[UnitTest(name: "Soak tick baseline requires an empty repair pipeline", category: "Networking")]
		public static UnitTestResult StartRequiresEmptyRepairPipeline()
		{
			WorldUpdateBatcher.ResetSessionState();
			try
			{
				if (!WorldUpdateBatcher.IsRepairPipelineIdle)
					return UnitTestResult.Fail("A reset repair pipeline was reported as pending");
				WorldUpdateBatcher.QueueForTests(new WorldUpdatePacket.CellUpdate
				{
					Cell = 1,
					ReplaceType = SimMessages.ReplaceType.Replace,
				}, backgroundRepair: true);
				if (WorldUpdateBatcher.IsRepairPipelineIdle)
					return UnitTestResult.Fail("Staged repair work could cross the soak baseline");
				WorldUpdateBatcher.PackagePendingForTests();
				return !WorldUpdateBatcher.IsRepairPipelineIdle
				       && !WorldUpdateBatcher.RepairPipelineCountsAreIdle(0, 0, 1)
					? UnitTestResult.Pass("Staged, dispatched, and journaled repairs block the soak baseline")
					: UnitTestResult.Fail("Dispatched repair work disappeared from the soak baseline");
			}
			finally
			{
				WorldUpdateBatcher.ResetSessionState();
			}
		}

		[UnitTest(name: "Soak repair warmup runs before the counted tick boundary", category: "Networking")]
		public static UnitTestResult WarmupPrecedesCountedTickBoundary()
		{
			var packet = RoundTrip(new SoakTickRunPacket
			{
				RunId = 8,
				SampleId = 3,
				TickCount = 1,
				StartTotalTime = 42f,
				IsRepairBaselineWarmup = true,
			});
			if (SoakStateHashProbe.RepairBaselineTickCount != 1
			    || packet.TickCount != 1 || !packet.IsRepairBaselineWarmup)
				return UnitTestResult.Fail("Pending repair warmup did not use a one-tick barrier");
			if (!SoakStateHashProbe.IsFirstCountedSegment(1, 0)
			    || SoakStateHashProbe.IsFirstCountedSegment(2, 0)
			    || SoakStateHashProbe.IsFirstCountedSegment(1, 1))
				return UnitTestResult.Fail("Warmup time could enter the fixed-tick run boundary");
			return UnitTestResult.Pass(
				"Pending repairs use an uncounted fixed-step run before segment one tick zero");
		}

		private static T RoundTrip<T>(T source) where T : IPacket, new()
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(
			       stream, System.Text.Encoding.UTF8, leaveOpen: true))
				source.Serialize(writer);
			stream.Position = 0;
			var copy = new T();
			using var reader = new BinaryReader(stream);
			copy.Deserialize(reader);
			return copy;
		}
	}
}
#endif
