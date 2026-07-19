using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Patches.World.SideScreen;
using Shared.Interfaces.Networking;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class AssignmentAuthorityTests
	{
		[UnitTest(name: "Assignments require verified client requests and host outcomes", category: "Sync")]
		public static UnitTestResult Authority()
		{
			var direct = new DispatchContext(19, false);
			DispatchContext verified = direct.AsVerifiedHostBroadcast();
			if (new AssignmentRequestPacket() is not IClientRelayable ||
			    new AssignmentPacket() is not IHostOnlyPacket)
				return UnitTestResult.Fail("Assignment authority marker is missing");
			if (AssignmentRequestPacket.ShouldAccept(true, direct, true) ||
			    AssignmentRequestPacket.ShouldAccept(true, verified, false) ||
			    !AssignmentRequestPacket.ShouldAccept(true, verified, true) ||
			    AssignmentRequestPacket.ShouldAccept(false, verified, true) ||
			    !AssignmentPacket.ShouldApply(false, true) ||
			    AssignmentPacket.ShouldApply(true, true) ||
			    AssignmentPacket.ShouldApply(false, false))
				return UnitTestResult.Fail("Assignment authority gate is incorrect");
			return UnitTestResult.Pass("Clients request through verified relay and only host outcomes mutate peers");
		}

		[UnitTest(name: "Assignment target and assignee are bounded absolute state", category: "Sync")]
		public static UnitTestResult RoundtripAndBounds()
		{
			var input = new AssignmentPacket(new AssignmentData
			{
				TargetNetId = -101,
				Cell = 123,
				AssigneeKind = AssignmentAssigneeKind.Entity,
				AssigneeNetId = -202,
				SlotId = "Bed.2"
			});
			AssignmentPacket output = Roundtrip(input, new AssignmentPacket());
			if (output.Data.TargetNetId != -101 || output.Data.Cell != 123 ||
			    output.Data.AssigneeKind != AssignmentAssigneeKind.Entity ||
			    output.Data.AssigneeNetId != -202 || output.Data.SlotId != "Bed.2")
				return UnitTestResult.Fail("Signed identity or exact assignment slot was lost");

			output.Data.SlotId = new string('s', AssignmentData.MaxSlotIdLength + 1);
			if (output.Data.IsWireValid())
				return UnitTestResult.Fail("Oversized assignment slot ID was accepted");
			output.Data.SlotId = "Bed.2";
			output.Data.AssigneeKind = AssignmentAssigneeKind.Group;
			output.Data.AssigneeNetId = 0;
			output.Data.GroupId = "public";
			if (output.Data.IsWireValid())
				return UnitTestResult.Fail("A non-entity assignment accepted a specific slot");

			output.Data.AssigneeKind = AssignmentAssigneeKind.Group;
			output.Data.AssigneeNetId = 0;
			output.Data.SlotId = "";
			output.Data.GroupId = new string('g', AssignmentData.MaxGroupIdLength + 1);
			if (output.Data.IsWireValid())
				return UnitTestResult.Fail("Oversized assignment group ID was accepted");
			output.Data.GroupId = "public";
			output.Data.Cell = AssignmentData.MaxCell;
			if (output.Data.IsWireValid())
				return UnitTestResult.Fail("Out-of-range assignment cell was accepted");
			return UnitTestResult.Pass("Assignment state preserves signed IDs and rejects unbounded fields");
		}

		[UnitTest(name: "Assignment cell cannot override registered identity", category: "Sync")]
		public static UnitTestResult IdentityBinding()
		{
			if (!AssignmentSync.IdentityMatches(-17, 45, -17, 45, -17) ||
			    AssignmentSync.IdentityMatches(-17, 45, -18, 45, -17) ||
			    AssignmentSync.IdentityMatches(-17, 45, -17, 46, -17) ||
			    AssignmentSync.IdentityMatches(-17, 45, -17, 45, -19))
				return UnitTestResult.Fail("Cell fallback can replace or relabel a registered identity");
			return UnitTestResult.Pass("NetId, cell, and deterministic building identity must all agree");
		}

		[UnitTest(name: "Assignment client mutation is request-only", category: "Sync")]
		public static UnitTestResult ClientMutationGate()
		{
			if (!Assignable_Assign_Patch.ShouldRunLocally(false, false, false) ||
			    !Assignable_Assign_Patch.ShouldRunLocally(true, true, false) ||
			    !Assignable_Assign_Patch.ShouldRunLocally(true, false, true) ||
			    Assignable_Assign_Patch.ShouldRunLocally(true, false, false) ||
			    !AssignmentSync.ShouldRequireCanAssign(true) ||
			    AssignmentSync.ShouldRequireCanAssign(false) ||
			    !Assignable_CanAssignTo_Patch.ShouldTrustHostOutcome(true, true) ||
			    Assignable_CanAssignTo_Patch.ShouldTrustHostOutcome(false, true) ||
			    Assignable_CanAssignTo_Patch.ShouldTrustHostOutcome(true, false))
				return UnitTestResult.Fail("Client assignment mutation was not suppressed");
			return UnitTestResult.Pass("Only offline, host, or incoming host state mutates assignments");
		}

		[UnitTest(name: "Assignment two-argument calls are request-only and recursion guarded", category: "Sync")]
		public static UnitTestResult SpecificSlotMutationGate()
		{
			System.Reflection.MethodInfo target = HarmonyLib.AccessTools.Method(typeof(Assignable),
				nameof(Assignable.Assign), new[] { typeof(IAssignableIdentity), typeof(AssignableSlotInstance) });
			if (target == null || target.ReturnType != typeof(void))
				return UnitTestResult.Fail("Two-argument Assign target changed in the game assembly");
			if (!Assignable_Assign_SpecificSlot_Patch.ShouldRunLocally(false, false, false) ||
			    !Assignable_Assign_SpecificSlot_Patch.ShouldRunLocally(true, true, false) ||
			    !Assignable_Assign_SpecificSlot_Patch.ShouldRunLocally(true, false, true) ||
			    Assignable_Assign_SpecificSlot_Patch.ShouldRunLocally(true, false, false))
				return UnitTestResult.Fail("Two-argument client assignment was not request-only");
			return UnitTestResult.Pass("Exact-slot assignments run only offline, on host, or under apply guard");
		}

		[UnitTest(name: "Assignment groups use verified requests and host outcomes", category: "Sync")]
		public static UnitTestResult GroupMembershipAuthority()
		{
			var direct = new DispatchContext(23, false);
			DispatchContext verified = direct.AsVerifiedHostBroadcast();
			if (new AssignmentGroupMemberRequestPacket() is not IClientRelayable ||
			    new AssignmentGroupMemberStatePacket() is not IHostOnlyPacket ||
			    AssignmentGroupMemberRequestPacket.ShouldAccept(true, direct, true) ||
			    AssignmentGroupMemberRequestPacket.ShouldAccept(true, verified, false) ||
			    !AssignmentGroupMemberRequestPacket.ShouldAccept(true, verified, true) ||
			    !AssignmentGroupMemberStatePacket.ShouldApply(false, true) ||
			    AssignmentGroupMemberStatePacket.ShouldApply(true, true))
				return UnitTestResult.Fail("Assignment group authority gate is incorrect");
			if (!AssignmentGroupController_SetMember_Patch.ShouldRunLocally(false, false, false) ||
			    !AssignmentGroupController_SetMember_Patch.ShouldRunLocally(true, true, false) ||
			    !AssignmentGroupController_SetMember_Patch.ShouldRunLocally(true, false, true) ||
			    AssignmentGroupController_SetMember_Patch.ShouldRunLocally(true, false, false))
				return UnitTestResult.Fail("Assignment group client mutation was not suppressed");

			var input = new AssignmentGroupMemberStatePacket
			{
				ControllerNetId = -301,
				Cell = 77,
				MinionNetId = -302,
				IsMember = true
			};
			AssignmentGroupMemberStatePacket output = Roundtrip(
				input, new AssignmentGroupMemberStatePacket());
			if (output.ControllerNetId != -301 || output.Cell != 77 || output.MinionNetId != -302 ||
			    !output.IsMember)
				return UnitTestResult.Fail("Assignment group signed identities did not roundtrip");
			return UnitTestResult.Pass("Group membership is bounded host-authoritative state");
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
				throw new InvalidDataException("Assignment packet left unread bytes");
			return output;
		}
	}
}
