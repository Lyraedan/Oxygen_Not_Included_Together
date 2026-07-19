using System;
using ONI_Together.Patches.World;
using ONI_Together.Patches.World.Buildings;
using ONI_Together.Patches.KleiPatches;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class NameSyncGuardTests
	{
		[UnitTest(name: "Packet echo guards recover after exceptions", category: "Networking")]
		public static UnitTestResult PacketGuardsRecoverAfterExceptions()
		{
			UserNameablePatch.ResetPacketGuardForTests();
			MinionIdentity_Patches.ResetPacketGuardForTests();
			KAnimControllerBase_Patches.ResetOverridePacketGuardForTests();

			if (!GuardRecovers(UserNameablePatch.RunWithPacketGuard, () => UserNameablePatch.IsApplyingPacket)
			    || !GuardRecovers(MinionIdentity_Patches.RunWithPacketGuard, () => MinionIdentity_Patches.IsApplyingPacket)
			    || !GuardRecovers(KAnimControllerBase_Patches.RunWithOverridePacketGuard,
				    () => KAnimControllerBase_Patches.IsTogglingOverrideFromPacket))
				return UnitTestResult.Fail("A packet exception left echo suppression enabled");

			return UnitTestResult.Pass("Name and animation packet suppression is released in finally blocks");
		}

		private static bool GuardRecovers(System.Action<System.Action> runGuarded, System.Func<bool> isApplying)
		{
			try
			{
				runGuarded(() => throw new InvalidOperationException("expected test exception"));
			}
			catch (InvalidOperationException)
			{
				return !isApplying();
			}

			return false;
		}
	}
}
