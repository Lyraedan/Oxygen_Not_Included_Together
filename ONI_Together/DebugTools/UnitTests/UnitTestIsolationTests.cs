using System.Reflection;

namespace ONI_Together.DebugTools.UnitTests;

public static class UnitTestIsolationTests
{
	private static int _invocationCount;

	[UnitTest(name: "Unit tests: active sessions run only live-safe tests", category: "Debug")]
	public static UnitTestResult ActiveSessionRunsOnlyLiveSafeTests()
	{
		MethodInfo probeMethod = typeof(UnitTestIsolationTests).GetMethod(
			nameof(CountInvocation), BindingFlags.Static | BindingFlags.NonPublic);
		if (probeMethod == null)
			return UnitTestResult.Fail("Isolation probe method could not be resolved");

		_invocationCount = 0;
		var unsafeTest = new UnitTest("unsafe", "Debug", false, probeMethod);
		unsafeTest.Run(liveSession: true);
		if (_invocationCount != 0 || unsafeTest.State != TestState.NotRun)
			return UnitTestResult.Fail("An unsafe test executed during an active session");

		var liveSafeTest = new UnitTest("safe", "Debug", true, probeMethod);
		liveSafeTest.Run(liveSession: true);
		if (_invocationCount != 1 || liveSafeTest.State != TestState.Passed)
			return UnitTestResult.Fail("A live-safe test was not executed during an active session");

		return UnitTestResult.Pass("Active sessions skip unsafe tests and execute live-safe tests");
	}

	private static UnitTestResult CountInvocation()
	{
		_invocationCount++;
		return UnitTestResult.Pass();
	}
}
