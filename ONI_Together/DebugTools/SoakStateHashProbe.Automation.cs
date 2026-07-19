#if DEBUG
using System;

namespace ONI_Together.DebugTools
{
	internal sealed partial class SoakStateHashProbe
	{
		internal static DebugCommandOutcome Start()
		{
			if (_instance == null)
				return DebugCommandOutcome.Fail("soak", "probe-unavailable");
			return StartIdempotently(() => _instance._running, _instance.TryStart);
		}

		internal static DebugCommandOutcome StartIdempotently(
			Func<bool> isRunning,
			Func<DebugCommandOutcome> start)
			=> isRunning()
				? DebugCommandOutcome.Ok("soak", "already-running")
				: start();
	}
}
#endif
