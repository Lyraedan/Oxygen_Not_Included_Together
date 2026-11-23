using System.Collections.Generic;
using ONI_Together.Networking;
using ONI_Together.Patches.World.SideScreen;
using UnityEngine;
using static DetailsScreen;

namespace ONI_Together.DebugTools.UnitTests
{
	/// <summary>
	/// Regression tests for the side-screen sync patches.
	/// </summary>
	public static class SideScreenTests
	{
		[UnitTest(name: "Slider handlers do not accumulate across SetTarget", category: "Sync")]
		public static UnitTestResult SliderHandlersDoNotAccumulate()
		{
			if (!MultiplayerSession.InActiveSession)
				return UnitTestResult.Fail("Not in a multiplayer session");

			var details = DetailsScreen.Instance;
			if (details == null)
				return UnitTestResult.Fail("DetailsScreen instance is null");

			// Publicized: read the private field directly instead of Traverse.
			var sideScreens = details.sideScreens;
			if (sideScreens == null)
				return UnitTestResult.Fail("Could not read DetailsScreen.sideScreens");

			SingleSliderSideScreen screen = null;
			foreach (var screenRef in sideScreens)
			{
				if (screenRef.screenPrefab is SingleSliderSideScreen ss)
				{
					screen = ss;
					break;
				}
			}
			if (screen == null)
				return UnitTestResult.Fail("SingleSliderSideScreen not found in DetailsScreen");

			var target = details.target;
			if (target == null)
				return UnitTestResult.Fail("No building selected - select a slider-controlled building and rerun");

			// Re-target the same building twice. The patch must replace the handler, not
			// accumulate one per call (the regression behind issues #57/#58).
			screen.SetTarget(target);
			int afterFirst = SideScreenHandlerCache.ReleaseHandlers.Count;
			screen.SetTarget(target);
			int afterSecond = SideScreenHandlerCache.ReleaseHandlers.Count;

			if (afterSecond != afterFirst)
				return UnitTestResult.Fail($"Slider handler cache grew on re-target: {afterFirst} -> {afterSecond}");

			return UnitTestResult.Pass($"Slider handler cache stable across SetTarget ({afterFirst} handler(s))");
		}
	}
}
