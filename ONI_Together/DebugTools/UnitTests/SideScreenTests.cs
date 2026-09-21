using System.Collections.Generic;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Patches.World.SideScreen;
using UnityEngine;

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
			if (!MultiplayerSession.InSession)
				return UnitTestResult.Fail("Not in a multiplayer session");

			var details = DetailsScreen.Instance;
			if (details == null)
				return UnitTestResult.Fail("DetailsScreen instance is null");

			// Same reflection UIUtils.GetElements uses to reach the side-screen list.
			var sideScreens = Traverse.Create(details).Field("sideScreens").GetValue<List<SideScreenRef>>();
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

			var target = SelectTool.Instance?.selected;
			if (target == null)
				return UnitTestResult.Fail("No building selected - select a slider-controlled building and rerun");

			if (target.GetComponent<ISliderControl>() == null && target.GetComponent<ISingleSliderControl>() == null)
				return UnitTestResult.Fail("Selected object has no slider control");

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
