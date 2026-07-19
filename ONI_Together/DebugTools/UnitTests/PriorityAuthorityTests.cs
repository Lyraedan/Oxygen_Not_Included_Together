using System.IO;
using HarmonyLib;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Tools.Prioritize;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.Packets.DuplicantActions;
using ONI_Together.Patches.World;
using Shared.Interfaces.Networking;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class PriorityAuthorityTests
	{
		[UnitTest(name: "Priority Harmony targets match build 740622", category: "Sync")]
		public static UnitTestResult HarmonyTargets()
		{
			if (AccessTools.Method(typeof(PrioritizeTool), nameof(PrioritizeTool.OnDragTool),
				    new[] { typeof(int), typeof(int) }) == null ||
			    AccessTools.Method(typeof(Prioritizable), "SetMasterPriority",
				    new[] { typeof(PrioritySetting) }) == null ||
			    AccessTools.Method(typeof(UserMenuScreen), "OnPriorityClicked",
				    new[] { typeof(PrioritySetting) }) == null)
				return UnitTestResult.Fail("A priority Harmony target changed");
			return UnitTestResult.Pass("Priority Harmony targets match build 740622");
		}

		[UnitTest(name: "Priority requests require verified relay and outcomes require host", category: "Sync")]
		public static UnitTestResult Authority()
		{
			var direct = new DispatchContext(9, false);
			var verified = direct.AsVerifiedHostBroadcast();
			if (new PrioritizePacket() is not IClientRelayable ||
			    new PrioritizeTargetRequestPacket() is not IClientRelayable ||
			    new PrioritizeStatePacket() is not IHostOnlyPacket)
				return UnitTestResult.Fail("Priority packet authority marker is missing");
			if (PrioritizeTargetRequestPacket.ShouldAccept(true, direct, true) ||
			    PrioritizeTargetRequestPacket.ShouldAccept(true, verified, false) ||
			    !PrioritizeTargetRequestPacket.ShouldAccept(true, verified, true) ||
			    PrioritizeTargetRequestPacket.ShouldAccept(false, verified, true) ||
			    PrioritizePacket.ShouldAccept(true, direct, true) ||
			    PrioritizePacket.ShouldAccept(true, verified, false) ||
			    !PrioritizePacket.ShouldAccept(true, verified, true) ||
			    PrioritizePacket.ShouldAccept(false, verified, true) ||
			    !PrioritizeStatePacket.ShouldApply(false, true) ||
			    PrioritizeStatePacket.ShouldApply(true, true) ||
			    PrioritizeStatePacket.ShouldApply(false, false))
				return UnitTestResult.Fail("Priority authority gate is incorrect");
			return UnitTestResult.Pass("Clients send verified requests and only host outcomes mutate peers");
		}

		[UnitTest(name: "Priority request and absolute outcome are bounded", category: "Sync")]
		public static UnitTestResult RoundtripAndBounds()
		{
			PrioritizeTargetRequestPacket request = Roundtrip(new PrioritizeTargetRequestPacket
			{
				NetId = -17,
				PriorityClass = (int)PriorityScreen.PriorityClass.high,
				PriorityValue = 9
			}, new PrioritizeTargetRequestPacket());
			if (request.NetId != -17 || request.PriorityClass != (int)PriorityScreen.PriorityClass.high ||
			    request.PriorityValue != 9)
				return UnitTestResult.Fail("Priority target request did not roundtrip");

			var state = new PrioritizeStatePacket();
			state.Priorities.Add(new PrioritizeStatePacket.PriorityData
			{
				NetId = -23,
				PriorityClass = (int)PriorityScreen.PriorityClass.topPriority,
				PriorityValue = 1
			});
			PrioritizeStatePacket output = Roundtrip(state, new PrioritizeStatePacket());
			if (output.Priorities.Count != 1 || output.Priorities[0].NetId != -23 ||
			    output.Priorities[0].PriorityClass != (int)PriorityScreen.PriorityClass.topPriority ||
			    output.Priorities[0].PriorityValue != 1)
				return UnitTestResult.Fail("Priority absolute outcome did not roundtrip");

			if (!PriorityAuthority.IsValidClientPriority(
				    new PrioritySetting(PriorityScreen.PriorityClass.basic, 1)) ||
			    !PriorityAuthority.IsValidClientPriority(
				    new PrioritySetting(PriorityScreen.PriorityClass.high, 9)) ||
			    !PriorityAuthority.IsValidClientPriority(
				    new PrioritySetting(PriorityScreen.PriorityClass.topPriority, 1)) ||
			    PriorityAuthority.IsValidClientPriority(
				    new PrioritySetting(PriorityScreen.PriorityClass.compulsory, 1)) ||
			    PriorityAuthority.IsValidClientPriority(
				    new PrioritySetting(PriorityScreen.PriorityClass.basic, 10)))
				return UnitTestResult.Fail("Priority client value bounds are incorrect");
			if (!Rejects(new PrioritizeTargetRequestPacket
			    {
				    NetId = 0,
				    PriorityClass = (int)PriorityScreen.PriorityClass.basic,
				    PriorityValue = 5
			    }) ||
			    !Rejects(new PrioritizeStatePacket
			    {
				    Priorities =
				    {
					    new PrioritizeStatePacket.PriorityData
					    {
						    NetId = 9,
						    PriorityClass = 99,
						    PriorityValue = 5
					    }
				    }
			    }))
				return UnitTestResult.Fail("Priority wire validation accepted an invalid payload");
			return UnitTestResult.Pass("Signed NetIds and valid user priorities roundtrip; invalid classes are rejected");
		}

		[UnitTest(name: "Priority client tool is request-only", category: "Sync")]
		public static UnitTestResult ClientToolGate()
		{
			if (!PrioritizeToolPatch.ShouldRunLocally(false, false, false) ||
			    !PrioritizeToolPatch.ShouldRunLocally(true, true, false) ||
			    !PrioritizeToolPatch.ShouldRunLocally(true, false, true) ||
			    PrioritizeToolPatch.ShouldRunLocally(true, false, false))
				return UnitTestResult.Fail("Priority client tool authority gate is incorrect");
			return UnitTestResult.Pass("Only offline, host, or incoming host execution runs the priority tool");
		}

		[UnitTest(name: "Duplicant priority requests are signed and bounded", category: "Sync")]
		public static UnitTestResult DuplicantPriorityBounds()
		{
			if (!DuplicantPriorityPacket.IsValidRequest(-7, "Research", 0)
			    || !DuplicantPriorityPacket.IsValidRequest(-7, "Research", 5)
			    || DuplicantPriorityPacket.IsValidRequest(0, "Research", 3)
			    || DuplicantPriorityPacket.IsValidRequest(-7, string.Empty, 3)
			    || DuplicantPriorityPacket.IsValidRequest(-7, "Research", -1)
			    || DuplicantPriorityPacket.IsValidRequest(-7, "Research", 6))
				return UnitTestResult.Fail("Duplicant priority validation accepted an unsigned or out-of-range request");
			if (!Rejects(new DuplicantPriorityPacket
			    {
				    NetId = 0,
				    ChoreGroupId = "Research",
				    Priority = 3
			    }))
				return UnitTestResult.Fail("Zero duplicant priority NetId serialized successfully");

			return UnitTestResult.Pass("Duplicant priority requests require nonzero NetIds and values 0 through 5");
		}

		private static T Roundtrip<T>(T input, T output) where T : IPacket
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				input.Serialize(writer);
			stream.Position = 0;
			using var reader = new BinaryReader(stream);
			output.Deserialize(reader);
			if (stream.Position != stream.Length)
				throw new InvalidDataException("Priority packet left unread bytes");
			return output;
		}

		private static bool Rejects(IPacket packet)
		{
			try
			{
				using var stream = new MemoryStream();
				using var writer = new BinaryWriter(stream);
				packet.Serialize(writer);
				return false;
			}
			catch (InvalidDataException)
			{
				return true;
			}
		}
	}
}
