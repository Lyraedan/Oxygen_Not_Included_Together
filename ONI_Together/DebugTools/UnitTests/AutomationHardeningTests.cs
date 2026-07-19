#if DEBUG
using System;
using System.Collections.Generic;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class AutomationHardeningTests
	{
		[UnitTest(name: "Debug command: pause is idempotent", category: "Debug")]
		public static UnitTestResult PauseCommandIsIdempotent()
		{
			bool paused = false;
			int pauseCount = 0;
			int publishCount = 0;
			DebugCommandOutcome first = DebugMenu.EnsurePausedForAutomation(
				isHost: true,
				isPaused: () => paused,
				setPaused: () => { paused = true; pauseCount++; },
				publishPaused: () => publishCount++);
			DebugCommandOutcome second = DebugMenu.EnsurePausedForAutomation(
				isHost: true,
				isPaused: () => paused,
				setPaused: () => { paused = true; pauseCount++; },
				publishPaused: () => publishCount++);

			if (!first.Success || !second.Success || pauseCount != 1 || publishCount != 2)
				return UnitTestResult.Fail(
					$"Pause outcomes were {first.Success}/{second.Success}; " +
					$"set={pauseCount}, published={publishCount}");
			return UnitTestResult.Pass("Repeated pause keeps the host paused and republishes Paused");
		}

		[UnitTest(name: "Debug command: soak start is idempotent", category: "Debug")]
		public static UnitTestResult SoakStartIsIdempotent()
		{
			bool running = false;
			int startCount = 0;
			Func<DebugCommandOutcome> start = () =>
			{
				startCount++;
				running = true;
				return DebugCommandOutcome.Ok("soak", "started");
			};

			DebugCommandOutcome first = SoakStateHashProbe.StartIdempotently(
				() => running, start);
			DebugCommandOutcome second = SoakStateHashProbe.StartIdempotently(
				() => running, start);
			if (!first.Success || !second.Success || startCount != 1
			    || second.Reason != "already-running")
				return UnitTestResult.Fail(
					$"Soak outcomes were {first.Success}/{second.Success}; starts={startCount}; " +
					$"secondReason={second.Reason}");
			return UnitTestResult.Pass("Repeated soak start leaves the active run untouched");
		}

		[UnitTest(name: "Debug command: outcome format is stable", category: "Debug")]
		public static UnitTestResult CommandOutcomeFormatIsStable()
		{
			string ok = DebugCommandOutcome.Ok("host", "already-hosting").ToLogLine();
			string fail = DebugCommandOutcome.Fail("join", "main-menu-required").ToLogLine();
			if (ok != "[DebugCommand][OK] command=host reason=already-hosting"
			    || fail != "[DebugCommand][FAIL] command=join reason=main-menu-required")
				return UnitTestResult.Fail($"Unexpected outcome lines: {ok} | {fail}");
			return UnitTestResult.Pass("Command outcomes have grep-stable OK/FAIL lines");
		}

		[UnitTest(name: "Unit tests: discovery exception fails summary", category: "Debug")]
		public static UnitTestResult DiscoveryExceptionFailsSummary()
		{
			var discovered = new List<UnitTest>();
			bool success = UnitTestRegistry.TryDiscoverTests(
				() => throw new InvalidOperationException("expected discovery failure"),
				discovered,
				out string failure);
			UnitTestRunSummary summary = UnitTestRegistry.CreateSummary(discovered, failure);
			var empty = new List<UnitTest>();
			bool emptySuccess = UnitTestRegistry.TryDiscoverTests(
				() => Array.Empty<Type>(), empty, out string emptyFailure);
			UnitTestRunSummary emptySummary = UnitTestRegistry.CreateSummary(empty, emptyFailure);

			if (success || string.IsNullOrEmpty(failure) || summary.Total != 1
			    || summary.Passed != 0 || summary.Failed != 1 || summary.Success
			    || emptySuccess || emptySummary.Total != 1 || emptySummary.Failed != 1)
				return UnitTestResult.Fail(
					$"Discovery success={success}; total={summary.Total}; " +
					$"passed={summary.Passed}; failed={summary.Failed}; " +
					$"emptySuccess={emptySuccess}; emptyFailed={emptySummary.Failed}; reason={failure}");
			return UnitTestResult.Pass("Discovery exceptions and empty discovery fail the summary");
		}
	}
}
#endif
