using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Tools.Build;
using ONI_Together.Patches.ToolPatches.Build;
using Shared.Interfaces.Networking;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class BuildAuthorityTests
	{
		[UnitTest(name: "Build requests require verified relay and outcomes require host", category: "Sync")]
		public static UnitTestResult AuthorityMarkersAndGates()
		{
			var direct = new DispatchContext(41, false);
			var verified = direct.AsVerifiedHostBroadcast();
			if (new BuildPacket() is not IClientRelayable ||
			    new UtilityBuildPacket() is not IClientRelayable ||
			    new BuildStatePacket() is not IHostOnlyPacket ||
			    new UtilityBuildStatePacket() is not IHostOnlyPacket)
				return UnitTestResult.Fail("Build request/state authority marker is missing");
			if (BuildPacket.ShouldAccept(true, direct, true) ||
			    BuildPacket.ShouldAccept(true, verified, false) ||
			    !BuildPacket.ShouldAccept(true, verified, true) ||
			    BuildPacket.ShouldAccept(false, verified, true) ||
			    UtilityBuildPacket.ShouldAccept(true, direct, true) ||
			    UtilityBuildPacket.ShouldAccept(true, verified, false) ||
			    !UtilityBuildPacket.ShouldAccept(true, verified, true) ||
			    UtilityBuildPacket.ShouldAccept(false, verified, true) ||
			    !BuildStatePacket.ShouldApply(false, true) ||
			    BuildStatePacket.ShouldApply(true, true) ||
			    BuildStatePacket.ShouldApply(false, false) ||
			    !UtilityBuildStatePacket.ShouldApply(false, true) ||
			    UtilityBuildStatePacket.ShouldApply(true, true) ||
			    UtilityBuildStatePacket.ShouldApply(false, false))
				return UnitTestResult.Fail("Build authority gate is incorrect");
			return UnitTestResult.Pass("Only verified client requests reach host mutation; only host state reaches clients");
		}

		[UnitTest(name: "Build request excludes client instant-build and state is absolute", category: "Sync")]
		public static UnitTestResult BuildRoundtrip()
		{
			var request = new BuildPacket
			{
				PrefabID = "ManualGenerator",
				Cell = 123,
				Orientation = Orientation.FlipH,
				MaterialTags = new List<string> { "Copper" },
				PriorityClass = (int)PriorityScreen.PriorityClass.high,
				PriorityValue = 8,
				ObjectLayer = (int)global::ObjectLayer.Building,
				FacadeID = "DEFAULT_FACADE"
			};
			BuildPacket copy = Roundtrip(request, new BuildPacket());
			if (copy.PrefabID != request.PrefabID || copy.Cell != request.Cell ||
			    copy.Orientation != request.Orientation || copy.MaterialTags.Count != 1 ||
			    copy.PriorityClass != request.PriorityClass || copy.PriorityValue != request.PriorityValue ||
			    copy.ObjectLayer != request.ObjectLayer || copy.FacadeID != request.FacadeID)
				return UnitTestResult.Fail("Build request did not roundtrip");

			BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
			if (typeof(BuildPacket).GetField("InstantBuild", fields) != null ||
			    typeof(BuildPacket).GetProperty("InstantBuild", fields) != null)
				return UnitTestResult.Fail("Client build request still carries InstantBuild");

			var state = BuildStatePacket.FromRequest(request, BuildPlacementKind.CompletedReplacement, true);
			state.NetId = 1234;
			state.LifecycleRevision = 55;
			BuildStatePacket stateCopy = Roundtrip(state, new BuildStatePacket());
			if (!stateCopy.InstantBuild || stateCopy.Outcome != BuildPlacementKind.CompletedReplacement ||
			    stateCopy.Cell != request.Cell || stateCopy.FacadeID != request.FacadeID
			    || stateCopy.NetId != 1234 || stateCopy.LifecycleRevision != 55)
				return UnitTestResult.Fail("Absolute build outcome did not roundtrip");
			return UnitTestResult.Pass("Client request cannot choose instant-build; host outcome carries exact placement kind");
		}

		[UnitTest(name: "Build policy validates orientation facade material and priority", category: "Sync")]
		public static UnitTestResult BuildPolicyBounds()
		{
			if (!BuildAuthority.IsWireCell(123) || BuildAuthority.IsWireCell(-1) ||
			    BuildAuthority.IsWireCell(BuildAuthority.MaxWireCell) ||
			    !BuildAuthority.IsOrientationAllowed(Orientation.Neutral, PermittedRotations.Unrotatable) ||
			    BuildAuthority.IsOrientationAllowed(Orientation.R90, PermittedRotations.Unrotatable) ||
			    !BuildAuthority.IsOrientationAllowed(Orientation.R270, PermittedRotations.R360) ||
			    BuildAuthority.IsOrientationAllowed(Orientation.FlipH, PermittedRotations.R360) ||
			    !BuildAuthority.IsOrientationAllowed(Orientation.FlipV, PermittedRotations.FlipV) ||
			    BuildAuthority.IsOrientationAllowed(Orientation.FlipH, PermittedRotations.FlipV))
				return UnitTestResult.Fail("Orientation policy accepted a rotation unavailable to the prefab");
			if (!BuildAuthority.IsPriorityAllowed((int)PriorityScreen.PriorityClass.basic, 1) ||
			    !BuildAuthority.IsPriorityAllowed((int)PriorityScreen.PriorityClass.high, 9) ||
			    !BuildAuthority.IsPriorityAllowed((int)PriorityScreen.PriorityClass.topPriority, 1) ||
			    BuildAuthority.IsPriorityAllowed((int)PriorityScreen.PriorityClass.compulsory, 1) ||
			    BuildAuthority.IsPriorityAllowed((int)PriorityScreen.PriorityClass.basic, 10))
				return UnitTestResult.Fail("Build priority policy accepted an unavailable value");
			if (!BuildAuthority.IsFacadeAllowed("DEFAULT_FACADE", Array.Empty<string>(), false, false) ||
			    !BuildAuthority.IsFacadeAllowed("skin_a", new[] { "skin_a" }, true, true) ||
			    BuildAuthority.IsFacadeAllowed("skin_b", new[] { "skin_a" }, true, true) ||
			    BuildAuthority.IsFacadeAllowed("skin_a", new[] { "skin_a" }, false, true) ||
			    BuildAuthority.IsFacadeAllowed("skin_a", new[] { "skin_a" }, true, false))
				return UnitTestResult.Fail("Facade policy accepted an unavailable skin");
			if (!BuildAuthority.IsMaterialAllowed(new Tag("Copper"), new[] { new Tag("Copper"), new Tag("Gold") }) ||
			    BuildAuthority.IsMaterialAllowed(new Tag("Iron"), new[] { new Tag("Copper"), new Tag("Gold") }))
				return UnitTestResult.Fail("Material policy accepted a tag outside its recipe category");
			if (!BuildAuthority.DeriveInstantBuild(true, false, false) ||
			    !BuildAuthority.DeriveInstantBuild(false, true, true) ||
			    BuildAuthority.DeriveInstantBuild(false, true, false) ||
			    BuildAuthority.DeriveInstantBuild(false, false, true))
				return UnitTestResult.Fail("Instant-build policy is not derived from host debug/sandbox state");
			return UnitTestResult.Pass("Build metadata is restricted to prefab and host policy");
		}

		[UnitTest(name: "Utility path is bounded unique and cardinally adjacent", category: "Sync")]
		public static UnitTestResult UtilityPathBounds()
		{
			if (!UtilityBuildAuthority.IsPathWireValid(new[] { 101, 102, 103 }) ||
			    UtilityBuildAuthority.IsPathWireValid(new[] { 5, 6, 5 }) ||
			    UtilityBuildAuthority.IsPathWireValid(new[] { BuildAuthority.MaxWireCell }) ||
			    !UtilityBuildAuthority.IsPathShapeValid(new[] { 5, 6, 10, 9 }, 4, 16) ||
			    UtilityBuildAuthority.IsPathShapeValid(new[] { 3, 4 }, 4, 16) ||
			    UtilityBuildAuthority.IsPathShapeValid(new[] { 5, 7 }, 4, 16) ||
			    UtilityBuildAuthority.IsPathShapeValid(new[] { 5, 6, 5 }, 4, 16) ||
			    UtilityBuildAuthority.IsPathShapeValid(Array.Empty<int>(), 4, 16) ||
			    UtilityBuildAuthority.IsPathShapeValid(new[] { -1 }, 4, 16) ||
			    UtilityBuildAuthority.IsPathShapeValid(new int[UtilityBuildAuthority.MaxPathNodeCount + 1], 4, 20000))
				return UnitTestResult.Fail("Utility path bounds or adjacency are incorrect");
			return UnitTestResult.Pass("Utility requests reject empty, wrapped, jumping, duplicate, and oversized paths");
		}

		[UnitTest(name: "Utility request excludes instant-build and outcome lists successful cells", category: "Sync")]
		public static UnitTestResult UtilityRoundtrip()
		{
			var request = new UtilityBuildPacket
			{
				PrefabID = "Wire",
				FacadeID = "DEFAULT_FACADE",
				Cells = new List<int> { 101, 102, 103 },
				MaterialTags = new List<string> { "Copper" },
				PriorityClass = (int)PriorityScreen.PriorityClass.basic,
				PriorityValue = 5,
				ObjectLayer = (int)global::ObjectLayer.Wire
			};
			UtilityBuildPacket copy = Roundtrip(request, new UtilityBuildPacket());
			if (copy.PrefabID != request.PrefabID || copy.Cells.Count != 3 || copy.Cells[2] != 103 ||
			    copy.MaterialTags.Count != 1 || copy.ObjectLayer != request.ObjectLayer)
				return UnitTestResult.Fail("Utility build request did not roundtrip");
			BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
			if (typeof(UtilityBuildPacket).GetField("InstantBuild", fields) != null ||
			    typeof(UtilityBuildPacket).GetProperty("InstantBuild", fields) != null)
				return UnitTestResult.Fail("Client utility request still carries InstantBuild");

			var state = UtilityBuildStatePacket.FromRequest(request, true, new List<UtilityBuildOutcome>
			{
				new UtilityBuildOutcome
				{
					Cell = 101, Kind = BuildPlacementKind.Completed,
					NetId = 2001, LifecycleRevision = 61
				},
				new UtilityBuildOutcome { Cell = 103, Kind = BuildPlacementKind.CompletedReplacement }
			});
			UtilityBuildStatePacket stateCopy = Roundtrip(state, new UtilityBuildStatePacket());
			if (!stateCopy.InstantBuild || stateCopy.Outcomes.Count != 2 ||
			    stateCopy.Outcomes[0].Cell != 101 ||
			    stateCopy.Outcomes[0].NetId != 2001 ||
			    stateCopy.Outcomes[0].LifecycleRevision != 61 ||
			    stateCopy.Outcomes[1].Kind != BuildPlacementKind.CompletedReplacement)
				return UnitTestResult.Fail("Utility absolute outcome did not roundtrip");
			return UnitTestResult.Pass("Utility request cannot choose instant-build; state carries only host successes");
		}

		[UnitTest(name: "Build clients are request-only", category: "Sync")]
		public static UnitTestResult ClientToolGate()
		{
			if (!BuildToolPatch.ShouldRunLocally(false, false, false) ||
			    !BuildToolPatch.ShouldRunLocally(true, true, false) ||
			    !BuildToolPatch.ShouldRunLocally(true, false, true) ||
			    BuildToolPatch.ShouldRunLocally(true, false, false) ||
			    !UtilityBuildToolPatch.ShouldRunLocally(false, false, false) ||
			    !UtilityBuildToolPatch.ShouldRunLocally(true, true, false) ||
			    !UtilityBuildToolPatch.ShouldRunLocally(true, false, true) ||
			    UtilityBuildToolPatch.ShouldRunLocally(true, false, false))
				return UnitTestResult.Fail("Build client tool authority gate is incorrect");
			return UnitTestResult.Pass("Only offline, host, or incoming host execution mutates through build tools");
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
				throw new InvalidDataException("Build packet left unread bytes");
			return output;
		}
	}
}
