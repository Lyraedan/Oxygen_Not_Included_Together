using System;
using ONI_Together.Networking.Packets.World.Handlers;
using ONI_Together.Patches.World.SideScreen;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class DlcInteractionTests
	{
		[UnitTest(name: "DLC interaction handler uses stable config hashes", category: "Sync")]
		public static UnitTestResult HandlerUsesStableConfigHashes()
		{
			if (SpiceGrinderWorkable_SetSelectedOption_Patch.ConfigHash != -163914648)
				return UnitTestResult.Fail("SpiceGrinderOption stable hash changed");
			if (Door_OrderUnseal_Patch.ConfigHash != 1281215108)
				return UnitTestResult.Fail("DoorUnseal stable hash changed");

			var hashes = new MiscBuildingHandler().SupportedConfigHashes;
			if (Array.IndexOf(hashes, SpiceGrinderWorkable_SetSelectedOption_Patch.ConfigHash) < 0)
				return UnitTestResult.Fail("SpiceGrinderOption is not registered");
			if (Array.IndexOf(hashes, Door_OrderUnseal_Patch.ConfigHash) < 0)
				return UnitTestResult.Fail("DoorUnseal is not registered");

			return UnitTestResult.Pass("DLC interaction config hashes are stable and registered");
		}

		[UnitTest(name: "Spice grinder option matching is exact and idempotent", category: "Sync")]
		public static UnitTestResult SpiceGrinderOptionMatchingIsExactAndIdempotent()
		{
			string[] optionIds = { "Salt", "Pepper", "Sugar" };
			if (SpiceGrinderWorkable_SetSelectedOption_Patch.FindOptionIndex(optionIds, "Pepper") != 1)
				return UnitTestResult.Fail("Known option was not matched");
			if (SpiceGrinderWorkable_SetSelectedOption_Patch.FindOptionIndex(optionIds, "pepper") != -1)
				return UnitTestResult.Fail("Option matching must be ordinal and case-sensitive");
			if (SpiceGrinderWorkable_SetSelectedOption_Patch.FindOptionIndex(optionIds, "Unknown") != -1)
				return UnitTestResult.Fail("Unknown option must be rejected");
			if (SpiceGrinderWorkable_SetSelectedOption_Patch.ShouldApplyOption("Pepper", "Pepper"))
				return UnitTestResult.Fail("Current option must not be applied again");
			if (!SpiceGrinderWorkable_SetSelectedOption_Patch.ShouldApplyOption("Salt", "Pepper"))
				return UnitTestResult.Fail("Different known option must be applied");

			return UnitTestResult.Pass("Option matching rejects unknown IDs and skips the current option");
		}
	}
}
