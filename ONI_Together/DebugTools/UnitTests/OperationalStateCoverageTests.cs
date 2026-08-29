using System.Collections.Generic;
using System.Text;
using ONI_Together.Networking;
using ONI_Together.Scripts.Buildings;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests
{
	/// <summary>
	/// The HasHostState gate stopped a client building reporting itself as running before the
	/// host had said anything, which is what was crashing SweepBotStation. That the crash went
	/// away was verified; that operational status still *displays* correctly afterwards was
	/// not, and it is the likeliest place for that change to have introduced a regression.
	///
	/// What this branch owns is the gate: without host state a building must not report itself
	/// as running. Whether that state reaches every building belongs to the delivery layer, so
	/// the coverage number is reported rather than asserted.
	///
	/// CLIENT ONLY, read-only, and worth running a few seconds after the world has settled -
	/// immediately after a join every building is legitimately still waiting.
	/// </summary>
	public static class OperationalStateCoverageTests
	{
		[UnitTest(name: "Operational: no client building claims unsent host state", category: "Sync")]
		public static UnitTestResult NoBuildingClaimsUnsentState()
		{
			// Not applicable is not a failure.
			if (!MultiplayerSession.InActiveSession)
				return UnitTestResult.Pass("skipped: not in a session");

			if (!MultiplayerSession.IsClient)
				return UnitTestResult.Pass("skipped: client only");

			var receivers = Object.FindObjectsByType<ClientReceiver_Operational>(FindObjectsSortMode.None);
			if (receivers == null || receivers.Length == 0)
				return UnitTestResult.Pass("skipped: no gated buildings in the scene yet");

			int withState = 0;
			int claimingWithoutState = 0;
			int operational = 0, functional = 0, active = 0;

			foreach (var wrap in receivers)
			{
				if (!wrap.HasHostState)
				{
					// The point of the gate: with no host state the wrapper must not be able to
					// report a building as running.
					if (wrap.IsOperational || wrap.IsFunctional || wrap.IsActive)
						claimingWithoutState++;

					continue;
				}

				withState++;
				if (wrap.IsOperational) operational++;
				if (wrap.IsFunctional) functional++;
				if (wrap.IsActive) active++;
			}

			if (claimingWithoutState > 0)
				return UnitTestResult.Fail(
					$"{claimingWithoutState} of {receivers.Length} buildings claim a state the host never sent");

			// Coverage is reported, not asserted. Whether host state reaches every building is
			// the delivery layer's business, and it was already unreliable before this gate -
			// the old default of true simply hid it.
			return UnitTestResult.Pass(
				$"no building claims unsent state; {withState}/{receivers.Length} have it "
				+ $"(operational {operational}, functional {functional}, active {active})");
		}
	}
}
