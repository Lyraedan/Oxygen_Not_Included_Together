using ONI_Together.Networking;
using System;

namespace ONI_Together.DebugTools.UnitTests;

public static class ReconnectFailureTests
{
	[UnitTest(name: "In-game handshake mismatch is terminal", category: "Networking")]
	public static UnitTestResult InGameMismatchCannotRemainConnected()
	{
		if (!GameClient.ShouldTerminateConnectionValidation(inMenu: false)
		    || GameClient.ShouldTerminateConnectionValidation(inMenu: true))
			return UnitTestResult.Fail("World-load validation mismatch could remain silently connected");

		return UnitTestResult.Pass("In-game validation failures terminate the reconnect state machine");
	}

	[UnitTest(name: "Reconnect start failures retain finite retry policy", category: "Networking")]
	public static UnitTestResult ReconnectStartFailureUsesFiniteRetryPolicy()
	{
		if (!GameClient.ShouldRetryReconnectStartFailure(inGame: true, attempt: 1)
		    || GameClient.ShouldRetryReconnectStartFailure(inGame: false, attempt: 1)
		    || GameClient.ShouldRetryReconnectStartFailure(inGame: true, attempt: 5))
			return UnitTestResult.Fail("Reconnect start failure can loop forever or stop before its retry budget");

		return UnitTestResult.Pass("Reconnect startup uses the same bounded retry budget as transport loss");
	}

	[UnitTest(name: "Approved world-load failure is terminal and single-shot", category: "Networking")]
	public static UnitTestResult ApprovedWorldLoadFailureIsSingleShot()
	{
		int loadAttempts = 0;
		int failures = 0;
		Exception observed = null;
		bool completed = ReadyManager.TryRunApprovedWorldLoad(
			() =>
			{
				loadAttempts++;
				throw new InvalidOperationException("synthetic load failure");
			},
			exception =>
			{
				failures++;
				observed = exception;
			});
		if (completed || loadAttempts != 1 || failures != 1
		    || observed is not InvalidOperationException)
		{
			return UnitTestResult.Fail("Approved load was retried or its exception escaped terminal handling");
		}

		return UnitTestResult.Pass("Approved load exceptions are consumed once by terminal cleanup");
	}
}
